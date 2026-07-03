using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.ComponentModel;
using Newtonsoft.Json;

using System.Reflection;
using System.Collections;
using System.Collections.ObjectModel;

using ICities;
using UnityEngine;
using ColossalFramework.UI;
using ColossalFramework.Plugins;

namespace NetworkAPI
{
    public class Network
    {
        public object HandleRequest(string jsonRequest)
        {
            object retObj = null;
            Request request;
            // parse the message according to Request formatting
            try
            {
                request = JsonConvert.DeserializeObject<Request>(jsonRequest);
            }
            catch (Exception e)
            {
                throw new Exception("Error: request not properly formatted: " + e.Message);
            }

            // got well formatted message, now process it
            if (request.Method == MethodType.GET)
            {
                retObj = GetObject(request.Object);
            }
            else if (request.Method == MethodType.SET)
            {

            }
            else if (request.Method == MethodType.EXECUTE)
            {

            }
            else
            {
                throw new Exception("Error: unsupported method type!");
            }

            return retObj;
        }

        public object GetObject(NetworkObject obj)
        {
            object retObj = null;

            // get required/dependent context now (recursively)
            Type contextType = null;
            object ctx = null;
            if (obj.Dependency != null)
            {
                ctx = GetObject(obj.Dependency);
            }
            if (!object.ReferenceEquals(ctx, null))
            {
                Type ctxAsType = ctx as Type;
                if (!object.ReferenceEquals(ctxAsType, null))
                {
                    contextType = ctxAsType;
                    ctx = null;
                }
                else
                {
                    contextType = ctx.GetType();
                }
                if (obj.IsStatic)
                {
                    ctx = null;
                }
            }

            /*
            DebugOutputPanel.AddMessage(PluginManager.MessageType.Message,
                "Getting: " + obj.Name + " of type: " + obj.Type + " from context:" + ctx);
                */

            // get object data now
            if (obj.Type == ObjectType.CLASS)
            {
                Type t = GetAssemblyType(obj.Assembly, obj.Name);
                if (object.ReferenceEquals(t, null))
                {
                    throw new Exception("Couldn't get: " + obj.Name + " from assembly: " + obj.Assembly);
                }
                retObj = t;
            }
            else if (obj.Type == ObjectType.MEMBER || obj.Type == ObjectType.METHOD)
            {
                retObj = GetObjectMember(contextType, ctx, obj);
            }
            else if (obj.Type == ObjectType.PARAMETER) // do we need this type?
            {
            }
            else
            {
                throw new Exception("Usupported object type: "+obj.Type);
            }

            // set the value of the object if it exists
            if (obj.Value != null)
            {
                Type t = ResolveParameterType(obj);
                if (object.ReferenceEquals(t, null))
                {
                    throw new Exception("Error: unknown ValueType " + obj.ValueType);
                }
                if (t.IsEnum)
                {
                    retObj = Enum.Parse(t, obj.Value.ToString());
                }
                else
                {
                    retObj = Convert.ChangeType(obj.Value, t);
                }
            }

            return retObj;
        }

        private Type ResolveParameterType(NetworkObject obj)
        {
            Type t = Type.GetType(obj.ValueType);
            if (!object.ReferenceEquals(t, null))
            {
                return t;
            }
            if (!string.IsNullOrEmpty(obj.Assembly))
            {
                t = GetAssemblyType(obj.Assembly, obj.ValueType);
                if (!object.ReferenceEquals(t, null))
                {
                    return t;
                }
            }
            string[] primitiveAssemblies = new string[] { "mscorlib", "System" };
            for (int i = 0; i < primitiveAssemblies.Length; i++)
            {
                t = GetAssemblyType(primitiveAssemblies[i], obj.ValueType);
                if (!object.ReferenceEquals(t, null))
                {
                    return t;
                }
            }
            return null;
        }

        public object GetObjectMember(Type contextType, object ctx, NetworkObject obj)
        {
            object retObj = null;
            // make sure we have context!
            if (!object.ReferenceEquals(contextType, null))
            {
                // get parameters (if they exist)
                List<object> parameters = new List<object>();
                if (obj.Parameters != null)
                {
                    for (int i = 0; i < obj.Parameters.Count; i++)
                    {
                        object param = GetObject(obj.Parameters.ElementAt(i));
                        Debug.Log("NetworkAPI: Got parameter: " + param.ToString());
                        parameters.Add(param);
                    }
                }
                // now actually get the member
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (obj.IsStatic)
                {
                    flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                }
                MemberInfo[] mia = contextType.GetMember(obj.Name, flags);
                foreach (var mi in mia)
                {
                    if (mi.MemberType == MemberTypes.Method)
                    {
                        MethodInfo methodInfo = SelectMethod(contextType, obj.Name, parameters.Count);
                        if (object.ReferenceEquals(methodInfo, null))
                        {
                            methodInfo = (MethodInfo)mi;
                        }
                        if (methodInfo.IsGenericMethod)
                        {
                            methodInfo = methodInfo.MakeGenericMethod(contextType);
                        }
                        if (parameters.Count > 0
                            && (obj.Name == "GetValue" || obj.Name == "get_Item"))
                        {
                            parameters[0] = Convert.ToInt32(parameters[0]);
                        }
                        retObj = methodInfo.Invoke(ctx, parameters.ToArray());
                        break;
                    }
                    else if (mi.MemberType == MemberTypes.Property)
                    {
                        PropertyInfo pi = (PropertyInfo)mi;
                        MethodInfo accessor = pi.GetGetMethod(true);
                        if (!object.ReferenceEquals(accessor, null))
                        {
                            retObj = accessor.Invoke(ctx, null);
                        }
                        break;
                    }
                    else if (mi.MemberType == MemberTypes.Field)
                    {
                        FieldInfo fi = (FieldInfo)mi;
                        retObj = fi.GetValue(ctx);
                        break;
                    }
                }
            }
            return retObj;
        }

        private MethodInfo SelectMethod(Type contextType, string name, int parameterCount)
        {
            MethodInfo[] methods = contextType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != name)
                {
                    continue;
                }
                ParameterInfo[] parms = candidate.GetParameters();
                if (parms.Length != parameterCount)
                {
                    continue;
                }
                if (parameterCount == 1
                    && (name == "GetValue" || name == "get_Item")
                    && object.ReferenceEquals(parms[0].ParameterType, typeof(int)))
                {
                    return candidate;
                }
                if (parameterCount == 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        public Type GetAssemblyType(string assemblyName, string typeName)
        {
            return Assembly.Load(assemblyName).GetType(typeName);
        }

        public void SetValueFromString(object target, string propertyName, string propertyValue)
        {
            PropertyInfo pi = target.GetType().GetProperty(propertyName);
            Type t = pi.PropertyType;

            if (t.IsGenericType &&
                t.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
            {
                if (propertyValue == null)
                {
                    pi.SetValue(target, null, null);
                    return;
                }
                t = new NullableConverter(pi.PropertyType).UnderlyingType;
            }
            pi.SetValue(target, Convert.ChangeType(propertyValue, t), null);
        }
    }
}
