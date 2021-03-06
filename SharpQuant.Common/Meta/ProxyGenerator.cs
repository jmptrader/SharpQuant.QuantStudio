﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Threading;

namespace SharpQuant.Common.Meta
{
    public interface IProxy
    {
        bool IsDirty { get; set; }
    }

    public interface IEditable
    {
        bool IsDirty();
        void Reset();
    }

    static public class ProxyBuilder
    {
        private static AssemblyBuilder GetAsmBuilder(string name)
        {
            var assemblyBuilder = Thread.GetDomain().DefineDynamicAssembly(new AssemblyName { Name = name },
                AssemblyBuilderAccess.RunAndSave);       //NOTE: to save, use RunAndSave

            return assemblyBuilder;
        }

        public static Type CreateProxyType(IInterfaceDef idef, string nameSpace = "SharpQuant.Common.Meta")
        {
            var assemblyBuilder = GetAsmBuilder(idef.Name);

            var moduleBuilder = assemblyBuilder.DefineDynamicModule(string.Format("{0}.{1}", nameSpace, idef.Name),"test.dll"); //NOTE: to save, add "asdasd.dll" parameter

            var interfaceType = typeof(IEditable);
            var typeBuilder = moduleBuilder.DefineType(idef.Name,
                TypeAttributes.Public | TypeAttributes.Class);
            typeBuilder.AddInterfaceImplementation(interfaceType);

            //create our _isDirty field, which implements IProxy
            var field = CreateIsDirtyProperty(typeBuilder);

            // Generate a field for each property, which implements the T
            foreach (var property in idef.Properties)
                CreateProperty(typeBuilder, property,field);


            var generatedType = typeBuilder.CreateType();

            assemblyBuilder.Save(@"test.dll");  //NOTE: to save, uncomment

            return generatedType;
        }

        private static FieldBuilder CreateIsDirtyProperty(TypeBuilder typeBuilder)
        {
            var propType = typeof(bool);
            var field = typeBuilder.DefineField("___" + "IsDirty", propType, FieldAttributes.Private);
            var meth = typeBuilder.DefineMethod("IsDirty", MethodAttributes.Public| MethodAttributes.Virtual, CallingConventions.Standard, typeof(bool), Type.EmptyTypes);
            var currGetIl = meth.GetILGenerator();
            currGetIl.Emit(OpCodes.Ldarg_0);
            currGetIl.Emit(OpCodes.Ldfld, field);
            currGetIl.Emit(OpCodes.Ret);

            var pmeth = typeof(IEditable).GetMethod("IsDirty");
            typeBuilder.DefineMethodOverride(meth, pmeth);

            var meth2 = typeBuilder.DefineMethod("Reset", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.Standard, null, Type.EmptyTypes);
            var currGetIl2 = meth2.GetILGenerator();
            currGetIl2.Emit(OpCodes.Ldarg_0);
            currGetIl2.Emit(OpCodes.Ldc_I4_0);
            currGetIl2.Emit(OpCodes.Stfld, field);
            currGetIl2.Emit(OpCodes.Ret);

            var pmeth2 = typeof(IEditable).GetMethod("Reset");
            typeBuilder.DefineMethodOverride(meth2, pmeth2);
            return field;

        }

        private static void CreateProperty(TypeBuilder typeBuilder, IPropertyDef pdef, FieldBuilder isdirty)
        {
            var propertyName = pdef.Name;
            var propType = pdef.PropertyType.Type;

            //Define the field and the property 
            var field = typeBuilder.DefineField("_" + propertyName, propType, FieldAttributes.Private);
            var property = typeBuilder.DefineProperty(propertyName,
                                           System.Reflection.PropertyAttributes.None,
                                           propType,
                                           new[] { propType });

            const MethodAttributes getSetAttr = MethodAttributes.Public | MethodAttributes.HideBySig;

            // Define the "get" and "set" accessor methods
            var currGetPropMthdBldr = typeBuilder.DefineMethod("get_" + propertyName,
                                         getSetAttr,
                                         propType,
                                         Type.EmptyTypes);

            var currGetIl = currGetPropMthdBldr.GetILGenerator();
            currGetIl.Emit(OpCodes.Ldarg_0);
            currGetIl.Emit(OpCodes.Ldfld, field);
            currGetIl.Emit(OpCodes.Ret);

            var currSetPropMthdBldr = typeBuilder.DefineMethod("set_" + propertyName,
                                         getSetAttr,
                                         null,
                                         new[] { propType });

            //store value in private field and set the isdirty flag
            var currSetIl = currSetPropMthdBldr.GetILGenerator();
            currSetIl.Emit(OpCodes.Ldarg_0);
            currSetIl.Emit(OpCodes.Ldarg_1);
            currSetIl.Emit(OpCodes.Stfld, field);
            currSetIl.Emit(OpCodes.Ldarg_0);
            currSetIl.Emit(OpCodes.Ldc_I4_1);
            currSetIl.Emit(OpCodes.Stfld, isdirty);
            //currSetIl.Emit(OpCodes.Call, setIsDirtyMethod);
            currSetIl.Emit(OpCodes.Ret);


            //var keyAttribute = typeof(KeyAttribute);
            //var myConstructorInfo = keyAttribute.GetConstructor(new Type[] { });
            //var attributeBuilder = new CustomAttributeBuilder(myConstructorInfo, new object[] { });
            //property.SetCustomAttribute(attributeBuilder);


            property.SetGetMethod(currGetPropMthdBldr);
            property.SetSetMethod(currSetPropMthdBldr);

            //var getMethod = typeof(T).GetMethod("get_" + propertyName);
            //var setMethod = typeof(T).GetMethod("set_" + propertyName);
            //typeBuilder.DefineMethodOverride(currGetPropMthdBldr, getMethod);
            //typeBuilder.DefineMethodOverride(currSetPropMthdBldr, setMethod);
        }
    }
    


    //code adapted from 
    //https://github.com/StackExchange/dapper-dot-net/blob/master/Dapper.Contrib/SqlMapperExtensions.cs

    public static class ProxyGenerator
    {
        private static readonly Dictionary<Type, object> TypeCache = new Dictionary<Type, object>();

        private static AssemblyBuilder GetAsmBuilder(string name)
        {
            var assemblyBuilder = Thread.GetDomain().DefineDynamicAssembly(new AssemblyName { Name = name },
                AssemblyBuilderAccess.Run);       //NOTE: to save, use RunAndSave

            return assemblyBuilder;
        }

        public static T GetInterfaceProxy<T>()
        {
            Type typeOfT = typeof(T);

            object k;
            if (TypeCache.TryGetValue(typeOfT, out k))
            {
                return (T)k;
            }
            var assemblyBuilder = GetAsmBuilder(typeOfT.Name);

            var moduleBuilder = assemblyBuilder.DefineDynamicModule("SharpQuant.Common.Meta." + typeOfT.Name); //NOTE: to save, add "asdasd.dll" parameter

            var interfaceType = typeof(IProxy);
            var typeBuilder = moduleBuilder.DefineType(typeOfT.Name + "_" + Guid.NewGuid(),
                TypeAttributes.Public | TypeAttributes.Class);
            typeBuilder.AddInterfaceImplementation(typeOfT);
            typeBuilder.AddInterfaceImplementation(interfaceType);

            //create our _isDirty field, which implements IProxy
            var setIsDirtyMethod = CreateIsDirtyProperty(typeBuilder);

            // Generate a field for each property, which implements the T
            foreach (var property in typeof(T).GetProperties())
                CreateProperty<T>(typeBuilder, property.Name, property.PropertyType, setIsDirtyMethod);


            var generatedType = typeBuilder.CreateType();

            //assemblyBuilder.Save(name + ".dll");  //NOTE: to save, uncomment

            var generatedObject = Activator.CreateInstance(generatedType);

            TypeCache.Add(typeOfT, generatedObject);
            return (T)generatedObject;
        }


        private static MethodInfo CreateIsDirtyProperty(TypeBuilder typeBuilder)
        {
            var propType = typeof(bool);
            var field = typeBuilder.DefineField("_" + "IsDirty", propType, FieldAttributes.Private);
            var property = typeBuilder.DefineProperty("IsDirty",
                                           System.Reflection.PropertyAttributes.None,
                                           propType,
                                           new[] { propType });

            const MethodAttributes getSetAttr = MethodAttributes.Public | MethodAttributes.NewSlot | MethodAttributes.SpecialName |
                                                MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.HideBySig;

            // Define the "get" and "set" accessor methods
            var currGetPropMthdBldr = typeBuilder.DefineMethod("get_" + "IsDirty",
                                         getSetAttr,
                                         propType,
                                         Type.EmptyTypes);
            var currGetIl = currGetPropMthdBldr.GetILGenerator();
            currGetIl.Emit(OpCodes.Ldarg_0);
            currGetIl.Emit(OpCodes.Ldfld, field);
            currGetIl.Emit(OpCodes.Ret);
            var currSetPropMthdBldr = typeBuilder.DefineMethod("set_" + "IsDirty",
                                         getSetAttr,
                                         null,
                                         new[] { propType });
            var currSetIl = currSetPropMthdBldr.GetILGenerator();
            currSetIl.Emit(OpCodes.Ldarg_0);
            currSetIl.Emit(OpCodes.Ldarg_1);
            currSetIl.Emit(OpCodes.Stfld, field);
            currSetIl.Emit(OpCodes.Ret);

            property.SetGetMethod(currGetPropMthdBldr);
            property.SetSetMethod(currSetPropMthdBldr);
            var getMethod = typeof(IProxy).GetMethod("get_" + "IsDirty");
            var setMethod = typeof(IProxy).GetMethod("set_" + "IsDirty");
            typeBuilder.DefineMethodOverride(currGetPropMthdBldr, getMethod);
            typeBuilder.DefineMethodOverride(currSetPropMthdBldr, setMethod);

            return currSetPropMthdBldr;
        }

        private static void CreateProperty<T>(TypeBuilder typeBuilder, string propertyName, Type propType, MethodInfo setIsDirtyMethod)
        {
            //Define the field and the property 
            var field = typeBuilder.DefineField("_" + propertyName, propType, FieldAttributes.Private);
            var property = typeBuilder.DefineProperty(propertyName,
                                           System.Reflection.PropertyAttributes.None,
                                           propType,
                                           new[] { propType });

            const MethodAttributes getSetAttr = MethodAttributes.Public | MethodAttributes.Virtual |
                                                MethodAttributes.HideBySig;

            // Define the "get" and "set" accessor methods
            var currGetPropMthdBldr = typeBuilder.DefineMethod("get_" + propertyName,
                                         getSetAttr,
                                         propType,
                                         Type.EmptyTypes);

            var currGetIl = currGetPropMthdBldr.GetILGenerator();
            currGetIl.Emit(OpCodes.Ldarg_0);
            currGetIl.Emit(OpCodes.Ldfld, field);
            currGetIl.Emit(OpCodes.Ret);

            var currSetPropMthdBldr = typeBuilder.DefineMethod("set_" + propertyName,
                                         getSetAttr,
                                         null,
                                         new[] { propType });

            //store value in private field and set the isdirty flag
            var currSetIl = currSetPropMthdBldr.GetILGenerator();
            currSetIl.Emit(OpCodes.Ldarg_0);
            currSetIl.Emit(OpCodes.Ldarg_1);
            currSetIl.Emit(OpCodes.Stfld, field);
            currSetIl.Emit(OpCodes.Ldarg_0);
            currSetIl.Emit(OpCodes.Ldc_I4_1);
            currSetIl.Emit(OpCodes.Call, setIsDirtyMethod);
            currSetIl.Emit(OpCodes.Ret);


            //var keyAttribute = typeof(KeyAttribute);
            //var myConstructorInfo = keyAttribute.GetConstructor(new Type[] { });
            //var attributeBuilder = new CustomAttributeBuilder(myConstructorInfo, new object[] { });
            //property.SetCustomAttribute(attributeBuilder);


            property.SetGetMethod(currGetPropMthdBldr);
            property.SetSetMethod(currSetPropMthdBldr);
            var getMethod = typeof(T).GetMethod("get_" + propertyName);
            var setMethod = typeof(T).GetMethod("set_" + propertyName);
            typeBuilder.DefineMethodOverride(currGetPropMthdBldr, getMethod);
            typeBuilder.DefineMethodOverride(currSetPropMthdBldr, setMethod);
        }
    }

 
}
