using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PostSharp.Aspects;
using CacheAspect.Supporting;
using System.Reflection;

namespace CacheAspect.Attributes
{
    public static partial class Cache
    {
        [Serializable]
        // TODO: Change name to CacheableAspect?
        public class Cacheable : OnMethodBoundaryAspect
        {
            private KeyBuilder _keyBuilder;
            public KeyBuilder KeyBuilder
            {
                get { return _keyBuilder ?? (_keyBuilder = new KeyBuilder()); }
            }

            #region Constructors

            /// <summary>
            /// Initializes a new instance of the Cacheable class
            /// </summary>
            public Cacheable()
                : this(string.Empty)
            {
            }

            /// <summary>
            /// Initializes a new instance of the Cacheable class
            /// </summary>
            /// <param name="groupName">Group name for the cached objects</param>
            public Cacheable(String groupName)
                : this(groupName, CacheSettings.Default)
            {
            }

            /// <summary>
            /// Initializes a new instance of the Cacheable class
            /// </summary>
            /// <param name="groupName">Group name for the cached objects</param>
            /// <param name="settings">An instance of CacheSettings for the settings of Cacheable</param>
            public Cacheable(String groupName, CacheSettings settings)
                : this(groupName, settings, string.Empty)
            {
            }

            /// <summary>
            /// Initializes a new instance of the Cacheable class
            /// </summary>
            /// <param name="groupName">Group name for the cached objects</param>
            /// <param name="settings">An instance of CacheSettings for the settings of Cacheable</param>
            /// <param name="parameterProperty">The name of the property to be used to generated the key for the cached object, when CacheSettings.UseProperty is used</param>
            public Cacheable(String groupName, CacheSettings settings, String parameterProperty)
                : this(groupName, settings, new string[] { parameterProperty })
            {
            }

            /// <summary>
            /// Initializes a new instance of the Cacheable class
            /// </summary>
            /// <param name="groupName">Group name for the cached objects</param>
            /// <param name="settings">An instance of CacheSettings for the settings of Cacheable</param>
            /// <param name="parameterProperties">The names of the properties to be used to generated the key for the cached object, when CacheSettings.UseProperty is used</param>
            public Cacheable(string groupName, CacheSettings settings, string[] parameterProperties)
            {
                KeyBuilder.GroupName = groupName;
                KeyBuilder.Settings = settings;
                KeyBuilder.ParameterProperties = parameterProperties;
            }

            #endregion

            //Method executed at build time.
            public override void CompileTimeInitialize(MethodBase method, AspectInfo aspectInfo)
            {
                KeyBuilder.MethodParameters = method.GetParameters();
                KeyBuilder.MethodName = string.Format("{0}.{1}", method.DeclaringType.FullName, method.Name);
            }

            // This method is executed before the execution of target methods of this aspect.
            public override void OnEntry(MethodExecutionArgs args)
            {
                // Compute the cache key.
                string cacheKey = KeyBuilder.BuildCacheKey(args.Instance, args.Arguments);

                // Fetch the value from the cache.
                ICache cache = CacheService.Cache;
                MethodExecWrapper value = (MethodExecWrapper)(cache.Contains(cacheKey) ? cache[cacheKey] : null);

                if (value != null && !IsTooOld(value.Timestamp))
                {
                    // The value was found in cache. Don't execute the method. Return immediately.
                    args.ReturnValue = value.ReturnValue;
                    // args.Arguments = value.Arguments;
                    for (int i = 0; i < value.Arguments.Length; i++)
                    {
                        // args.Arguments.SetArgument(i, value.Arguments[i]);
                        object fromArgs = args.Arguments[i];
                        object cached = value.Arguments[i];

                        if (cached == null)
                        {
                            continue;
                        }

                        if (fromArgs != null && cached != null &&
                            !object.ReferenceEquals(fromArgs.GetType(), cached.GetType()))
                        {
                            continue;
                        }

                        Type commonType = fromArgs.GetType();
                        foreach (PropertyInfo pi in commonType.GetProperties())
                        {
                            if (pi.CanRead && pi.CanWrite)
                            {
                                object fromValue = pi.GetValue(fromArgs, null);
                                object cachedValue = pi.GetValue(cached, null);
                                if (fromValue != cachedValue)
                                {
                                    pi.SetValue(fromArgs, cachedValue, null);
                                }
                            }
                        }
                    }
                    args.FlowBehavior = FlowBehavior.Return;
                }
                else
                {
                    // The value was NOT found in cache. Continue with method execution, but store
                    // the cache key so that we don't have to compute it in OnSuccess.
                    args.MethodExecutionTag = cacheKey;
                }
            }

            // This method is executed upon successful completion of target methods of this aspect.
            public override void OnSuccess(MethodExecutionArgs args)
            {
                string cacheKey = (string)args.MethodExecutionTag;
                CacheService.Cache[cacheKey] = new MethodExecWrapper
                {
                    ReturnValue = args.ReturnValue,
                    Timestamp = DateTime.UtcNow,
                    Arguments = args.Arguments.ToArray()
                };
            }

            private bool IsTooOld(DateTime time)
            {
                if (KeyBuilder.Settings == CacheSettings.IgnoreTTL)
                {
                    return false;
                }
                return DateTime.UtcNow - time > CacheService.TimeToLive;                
            }

        }
    }

}

