using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

#if DLL
namespace Invert.IOC
#else
namespace uFrame.IOC
#endif
{
    /// <summary>
    /// A ViewModel Container and a factory for Controllers and commands.
    /// </summary>

    public class UFrameContainer : IUFrameContainer

    {
        private readonly Dictionary<Type, TypeInjectionInfo> _typeInjectionInfos = new Dictionary<Type, TypeInjectionInfo>();
        private TypeInstanceCollection _instances;
        private TypeMappingCollection _mappings;


        public TypeMappingCollection Mappings
        {
            get { return _mappings ?? (_mappings = new TypeMappingCollection()); }
            set { _mappings = value; }
        }

        public TypeInstanceCollection Instances
        {
            get { return _instances ?? (_instances = new TypeInstanceCollection()); }
            set { _instances = value; }
        }

        public TypeRelationCollection RelationshipMappings
        {
            get { return _relationshipMappings; }
            set { _relationshipMappings = value; }
        }

        public IEnumerable<TType> ResolveAll<TType>()
        {
            foreach (var obj in ResolveAll(typeof(TType)))
            {
                yield return (TType)obj;
            }
        }

        /// <summary>
        /// Resolves all instances of TType or subclasses of TType.  Either named or not.
        /// </summary>
        /// <typeparam name="TType">The Type to resolve</typeparam>
        /// <returns>List of objects.</returns>
        public IEnumerable<object> ResolveAll(Type type)
        {
            foreach (KeyValuePair<Tuple<Type, string>, object> kv in Instances)
            {
                if(kv.Key.Item1 == type && !string.IsNullOrEmpty(kv.Key.Item2))
                    yield return kv.Value;
            }

            foreach (KeyValuePair<Tuple<Type, string>, Type> kv in Mappings)
            {
                if (!string.IsNullOrEmpty(kv.Key.Item2))
                {
#if NETFX_CORE 
                    var condition = type.GetTypeInfo().IsSubclassOf(mapping.From);
#else
                    var condition = type.IsAssignableFrom(kv.Key.Item1);
#endif
                    if (condition)
                    {
                        var item = Activator.CreateInstance(kv.Value);
                        Inject(item);
                        yield return item;
                    }
                }
            }
        }
        /// <summary>
        /// Clears all type-mappings and instances.
        /// </summary>
        public void Clear()
        {
            Instances.Clear();
            Mappings.Clear();
            RelationshipMappings.Clear();
        }

        /// <summary>
        /// Injects registered types/mappings into an object
        /// </summary>
        /// <param name="obj"></param>
        public void Inject(object obj)
        {
            if (obj == null) return;

            Type objectType = obj.GetType();

            TypeInjectionInfo typeInjectionInfo;
            if (!_typeInjectionInfos.TryGetValue(objectType, out typeInjectionInfo)) {
                List<TypeInjectionInfo.InjectionMemberInfo<PropertyInfo>> propertyInjectionInfos = new List<TypeInjectionInfo.InjectionMemberInfo<PropertyInfo>>();
                List<TypeInjectionInfo.InjectionMemberInfo<FieldInfo>> fieldInjectionInfos = new List<TypeInjectionInfo.InjectionMemberInfo<FieldInfo>>();
#if !NETFX_CORE
                var members = obj.GetType().GetMembers();
#else
                var members = obj.GetType().GetTypeInfo().DeclaredMembers;
#endif
                Type injectAttributeType = typeof(InjectAttribute);
                foreach (var memberInfo in members) {
                    InjectAttribute injectAttribute = (InjectAttribute) Attribute.GetCustomAttribute(memberInfo, injectAttributeType);
                    if (injectAttribute == null)
                        continue;

                    var propertyInfo = memberInfo as PropertyInfo;
                    if (propertyInfo != null)
                    {
                        propertyInjectionInfos.Add(new TypeInjectionInfo.InjectionMemberInfo<PropertyInfo>(propertyInfo, propertyInfo.PropertyType, injectAttribute.Name));
                        continue;
                    }

                    var fieldInfo = memberInfo as FieldInfo;
                    if (fieldInfo != null)
                    {
                        fieldInjectionInfos.Add(new TypeInjectionInfo.InjectionMemberInfo<FieldInfo>(fieldInfo, fieldInfo.FieldType, injectAttribute.Name));
                    }
                }

                typeInjectionInfo = new TypeInjectionInfo(propertyInjectionInfos.ToArray(), fieldInjectionInfos.ToArray());
                _typeInjectionInfos.Add(objectType, typeInjectionInfo);
            }

            for (int i = 0; i < typeInjectionInfo.PropertyInjectionInfos.Length; i++) {
                var injectionInfo = typeInjectionInfo.PropertyInjectionInfos[i];
                injectionInfo.MemberInfo.SetValue(obj, Resolve(injectionInfo.MemberType, injectionInfo.InjectName), null);
            }

            for (int i = 0; i < typeInjectionInfo.FieldInjectionInfos.Length; i++) {
                var injectionInfo = typeInjectionInfo.FieldInjectionInfos[i];
                injectionInfo.MemberInfo.SetValue(obj, Resolve(injectionInfo.MemberType, injectionInfo.InjectName));
            }
        }

        /// <summary>
        /// Register a type mapping
        /// </summary>
        /// <typeparam name="TSource">The base type.</typeparam>
        /// <typeparam name="TTarget">The concrete type</typeparam>
        public void Register<TSource, TTarget>(string name = null)
        {
            Mappings[typeof(TSource), name] = typeof(TTarget);
        }

        public void Register(Type source, Type target, string name = null)
        {
            Mappings[source, name] = target;
        }

        /// <summary>
        /// Register a named instance
        /// </summary>
        /// <param name="baseType">The type to register the instance for.</param>        
        /// <param name="instance">The instance that will be resolved be the name</param>
        /// <param name="injectNow">Perform the injection immediately</param>
        public void RegisterInstance(Type baseType, object instance = null, bool injectNow = true)
        {
            RegisterInstance(baseType, instance, null, injectNow);
        }

        /// <summary>
        /// Register a named instance
        /// </summary>
        /// <param name="baseType">The type to register the instance for.</param>
        /// <param name="name">The name for the instance to be resolved.</param>
        /// <param name="instance">The instance that will be resolved be the name</param>
        /// <param name="injectNow">Perform the injection immediately</param>
        public virtual void RegisterInstance(Type baseType, object instance = null, string name = null, bool injectNow = true)
        {
            Instances[baseType, name] = instance;
            if (injectNow)
            {
                Inject(instance);
            }
        }

        public void RegisterInstance<TBase>(TBase instance) where TBase : class
        {
            RegisterInstance<TBase>(instance, true);
        }

        public void RegisterInstance<TBase>(TBase instance, bool injectNow) where TBase : class
        {
            RegisterInstance<TBase>(instance, null, injectNow);
        }

        public void RegisterInstance<TBase>(TBase instance, string name, bool injectNow = true) where TBase : class
        {
            RegisterInstance(typeof(TBase), instance, name, injectNow);
        }

        /// <summary>
        ///  If an instance of T exist then it will return that instance otherwise it will create a new one based off mappings.
        /// </summary>
        /// <typeparam name="T">The type of instance to resolve</typeparam>
        /// <returns>The/An instance of 'instanceType'</returns>
        public T Resolve<T>(string name = null, bool requireInstance = false, params object[] args) where T : class
        {
            return (T)Resolve(typeof(T), name, requireInstance, args);
        }

        /// <summary>
        /// If an instance of instanceType exist then it will return that instance otherwise it will create a new one based off mappings.
        /// </summary>
        /// <param name="baseType">The type of instance to resolve</param>
        /// <param name="name">The type of instance to resolve</param>
        /// <param name="requireInstance">If true will return null if an instance isn't registered.</param>
        /// <param name="constructorArgs">The arguments to pass to the constructor if any.</param>
        /// <returns>The/An instance of 'instanceType'</returns>
        public object Resolve(Type baseType, string name = null, bool requireInstance = false, params object[] constructorArgs)
        {
            // Look for an instance first
            var item = Instances[baseType, name];
            if (item != null)
            {
                return item;
            }
            if (requireInstance)
                return null;
            // Check if there is a mapping of the type
            var namedMapping = Mappings[baseType, name];
            if (namedMapping != null)
            {
                var obj = CreateInstance(namedMapping, constructorArgs);
                //Inject(obj);
                return obj;
            }
            return null;
        }

        public object CreateInstance(Type type, params object[] constructorArgs)
        {
            if (constructorArgs != null && constructorArgs.Length > 0)
            {
                //return Activator.CreateInstance(type,BindingFlags.Public | BindingFlags.Instance,Type.DefaultBinder, constructorArgs,CultureInfo.CurrentCulture);
                var obj2 = Activator.CreateInstance(type, constructorArgs);
                Inject(obj2);
                return obj2;
            }
#if !NETFX_CORE
            ConstructorInfo[] constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
#else
            ConstructorInfo[] constructor = type.GetTypeInfo().DeclaredConstructors.ToArray();
#endif

            if (constructor.Length < 1)
            {
                var obj2 = Activator.CreateInstance(type);
                Inject(obj2);
                return obj2;
            }

            var maxParameters = constructor.First().GetParameters();

            foreach (var c in constructor)
            {
                var parameters = c.GetParameters();
                if (parameters.Length > maxParameters.Length)
                {
                    maxParameters = parameters;
                }

            }
            var args = maxParameters.Select(p =>
            {
                if (p.ParameterType.IsArray)
                {
                    return ResolveAll(p.ParameterType);
                }
                return Resolve(p.ParameterType) ?? Resolve(p.ParameterType, p.Name);
            }).ToArray();

            var obj = Activator.CreateInstance(type, args);
            Inject(obj);
            return obj;
        }

        public TBase ResolveRelation<TBase>(Type tfor, params object[] args)
        {
            try
            {
                return (TBase)ResolveRelation(tfor, typeof(TBase), args);
            }
            catch (InvalidCastException castIssue)
            {
                throw new Exception(string.Format("Resolve Relation couldn't cast  to {0} from {1}", typeof(TBase).Name, tfor.Name), castIssue);
            }
        }
        public void InjectAll()
        {
            foreach (object instance in Instances.Values)
            {
                Inject(instance);
            }
        }
        private TypeRelationCollection _relationshipMappings = new TypeRelationCollection();
        public void RegisterRelation<TFor, TBase, TConcrete>()
        {
            RelationshipMappings[typeof(TFor), typeof(TBase)] = typeof(TConcrete);
        }

        public void RegisterRelation(Type tfor, Type tbase, Type tconcrete)
        {
            RelationshipMappings[tfor, tbase] = tconcrete;
        }
        public object ResolveRelation(Type tfor, Type tbase, params object[] args)
        {
            var concreteType = RelationshipMappings[tfor, tbase];

            if (concreteType == null)
            {
                return null;
            }
            var result = CreateInstance(concreteType, args);
            //Inject(result);
            return result;
        }
        public TBase ResolveRelation<TFor, TBase>(params object[] arg)
        {
            return (TBase)ResolveRelation(typeof(TFor), typeof(TBase), arg);
        }

        private class TypeInjectionInfo
        {
            public readonly InjectionMemberInfo<PropertyInfo>[] PropertyInjectionInfos;
            public readonly InjectionMemberInfo<FieldInfo>[] FieldInjectionInfos;
        
            public TypeInjectionInfo(InjectionMemberInfo<PropertyInfo>[] propertyInjectionInfos, InjectionMemberInfo<FieldInfo>[] fieldInjectionInfos)
            {
                PropertyInjectionInfos = propertyInjectionInfos;
                FieldInjectionInfos = fieldInjectionInfos;
            }
        
            public class InjectionMemberInfo<T> where T : MemberInfo
            {
                public readonly T MemberInfo;
                public readonly Type MemberType;
                public readonly string InjectName;
        
                public InjectionMemberInfo(T memberInfo, Type memberType, string injectName)
                {
                    MemberInfo = memberInfo;
                    MemberType = memberType;
                    InjectName = injectName;
                }
            }
        }
    }

    // http://stackoverflow.com/questions/1171812/multi-key-dictionary-in-c
    public class Tuple<T1, T2>  //FUCKING Unity: struct is not supported in Mono
    {
        public readonly T1 Item1;
        public readonly T2 Item2;
        public Tuple(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }

        public override bool Equals(Object obj)
        {
            Tuple<T1, T2> p = obj as Tuple<T1, T2>;
            if(obj == null) return false;

            if(Item1 == null)
            {
                if (p.Item1 != null) return false;
            }
            else
            {
                if (p.Item1 == null || !Item1.Equals(p.Item1)) return false;
            }
            if (Item2 == null)
            {
                if (p.Item2 != null) return false;
            }
            else
            {
                if (p.Item2 == null || !Item2.Equals(p.Item2)) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 0;
            if (Item1 != null)
                hash ^= Item1.GetHashCode();
            if (Item2 != null)
                hash ^= Item2.GetHashCode();
            return hash;
        }
    }

    // Kanglai: Using Dictionary rather than List!
    public class TypeMappingCollection : Dictionary<Tuple<Type, string>, Type>
    {
        public Type this[Type from, string name = null]
        {
            get
            {
                Tuple<Type, string> key = new Tuple<Type, string>(from, name);
                Type mapping = null;
                if(this.TryGetValue(key, out mapping))
                {
                    return mapping;
                }
                return null;
            }
            set
            {
                Tuple<Type, string> key = new Tuple<Type, string>(from, name);
                this[key] = value;
            }
        }
    }
    public class TypeInstanceCollection : Dictionary<Tuple<Type, string>, object>
    {

        public object this[Type from, string name = null]
        {
            get
            {
                Tuple<Type, string> key = new Tuple<Type, string>(from, name);
                object mapping = null;
                if (this.TryGetValue(key, out mapping))
                {
                    return mapping;
                }
                return null;
            }
            set
            {
                Tuple<Type, string> key = new Tuple<Type, string>(from, name);
                this[key] = value;
            }
        }
    }
    public class TypeRelationCollection : Dictionary<Tuple<Type, Type>, Type>
    {
        
        public Type this[Type from, Type to]
        {
            get
            {
                Tuple<Type, Type> key = new Tuple<Type, Type>(from, to);
                Type mapping = null;
                if (this.TryGetValue(key, out mapping))
                {
                    return mapping;
                }
                return null;
            }
            set
            {
                Tuple<Type, Type> key = new Tuple<Type, Type>(from, to); 
                this[key] = value;
            }
        }
    }
    public class TypeRelation
    {
        public Type From
        {
            get;
            set;
        }

        public Type To { get; set; }

        public Type Concrete { get; set; }

        //public string Name { get; set; }
    }
    public class RegisteredInstance
    {
        public Type Base
        {
            get;
            set;
        }

        public object Instance
        {
            get;
            set;
        }

        public string Name { get; set; }
    }
    public class TypeMapping
    {
        public Type From
        {
            get;
            set;
        }

        public Type To
        {
            get;
            set;
        }
        public string Name { get; set; }
    }

}