﻿using System;
using System.Linq;
using System.Reflection;
using DCS.Alternative.Launcher.Collections;

namespace DCS.Alternative.Launcher.Extensions
{
    public static class TypeExtensions
    {
        private static readonly SafeDictionary<GenericMethodCacheKey, MethodInfo> _genericMethodCache;

        static TypeExtensions()
        {
            _genericMethodCache = new SafeDictionary<GenericMethodCacheKey, MethodInfo>();
        }

        /// <summary>
        ///     Gets a generic method from a type given the method name, binding flags, generic types and parameter types
        /// </summary>
        /// <param name="sourceType">Source type</param>
        /// <param name="bindingFlags">Binding flags</param>
        /// <param name="methodName">Name of the method</param>
        /// <param name="genericTypes">Generic types to use to make the method generic</param>
        /// <param name="parameterTypes">Method parameters</param>
        /// <returns>MethodInfo or null if no matches found</returns>
        /// <exception cref="System.Reflection.AmbiguousMatchException" />
        /// <exception cref="System.ArgumentException" />
        public static MethodInfo GetGenericMethod(
            this Type sourceType, BindingFlags bindingFlags, string methodName, Type[] genericTypes,
            Type[] parameterTypes)
        {
            MethodInfo method;
            var cacheKey = new GenericMethodCacheKey(sourceType, methodName, genericTypes, parameterTypes);

            // Shouldn't need any additional locking
            // we don't care if we do the method info generation
            // more than once before it gets cached.
            if (!_genericMethodCache.TryGetValue(cacheKey, out method))
            {
                method = GetMethod(sourceType, bindingFlags, methodName, genericTypes, parameterTypes);
                _genericMethodCache[cacheKey] = method;
            }

            return method;
        }

        private static MethodInfo GetMethod(Type sourceType, BindingFlags bindingFlags, string methodName,
            Type[] genericTypes, Type[] parameterTypes)
        {
#if GETPARAMETERS_OPEN_GENERICS
            var methods =
                sourceType.GetMethods(bindingFlags).Where(
                    mi => string.Equals(methodName, mi.Name, StringComparison.InvariantCulture)).Where(
                    mi => mi.ContainsGenericParameters).Where(mi => mi.GetGenericArguments().Length == genericTypes.Length).Where(mi => mi.GetParameters().Length == parameterTypes.Length).Select(
                    mi => mi.MakeGenericMethod(genericTypes)).Where(
                    mi => mi.GetParameters().Select(pi => pi.ParameterType).SequenceEqual(parameterTypes)).ToList();
#else
            var validMethods = from method in sourceType.GetMethods(bindingFlags)
                where method.Name == methodName
                where method.IsGenericMethod
                where method.GetGenericArguments().Length == genericTypes.Length
                let genericMethod = method.MakeGenericMethod(genericTypes)
                where genericMethod.GetParameters().Count() == parameterTypes.Length
                where genericMethod.GetParameters().Select(pi => pi.ParameterType).SequenceEqual(parameterTypes)
                select genericMethod;

            var methods = validMethods.ToList();
#endif
            if (methods.Count > 1)
            {
                throw new AmbiguousMatchException();
            }

            return methods.FirstOrDefault();
        }

        private sealed class GenericMethodCacheKey
        {
            private readonly Type[] _genericTypes;
            private readonly int _hashCode;
            private readonly string _methodName;
            private readonly Type[] _parameterTypes;
            private readonly Type _sourceType;

            public GenericMethodCacheKey(Type sourceType, string methodName, Type[] genericTypes, Type[] parameterTypes)
            {
                _sourceType = sourceType;
                _methodName = methodName;
                _genericTypes = genericTypes;
                _parameterTypes = parameterTypes;
                _hashCode = GenerateHashCode();
            }

            public override bool Equals(object obj)
            {
                var cacheKey = obj as GenericMethodCacheKey;
                if (cacheKey == null)
                {
                    return false;
                }

                if (_sourceType != cacheKey._sourceType)
                {
                    return false;
                }

                if (!string.Equals(_methodName, cacheKey._methodName, StringComparison.InvariantCulture))
                {
                    return false;
                }

                if (_genericTypes.Length != cacheKey._genericTypes.Length)
                {
                    return false;
                }

                if (_parameterTypes.Length != cacheKey._parameterTypes.Length)
                {
                    return false;
                }

                for (var i = 0; i < _genericTypes.Length; ++i)
                {
                    if (_genericTypes[i] != cacheKey._genericTypes[i])
                    {
                        return false;
                    }
                }

                for (var i = 0; i < _parameterTypes.Length; ++i)
                {
                    if (_parameterTypes[i] != cacheKey._parameterTypes[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            public override int GetHashCode()
            {
                return _hashCode;
            }

            private int GenerateHashCode()
            {
                unchecked
                {
                    var result = _sourceType.GetHashCode();

                    result = (result * 397) ^ _methodName.GetHashCode();

                    for (var i = 0; i < _genericTypes.Length; ++i)
                    {
                        result = (result * 397) ^ _genericTypes[i].GetHashCode();
                    }

                    for (var i = 0; i < _parameterTypes.Length; ++i)
                    {
                        result = (result * 397) ^ _parameterTypes[i].GetHashCode();
                    }

                    return result;
                }
            }
        }
    }
}