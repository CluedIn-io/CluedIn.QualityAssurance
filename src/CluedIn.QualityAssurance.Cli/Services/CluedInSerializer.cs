using Castle.MicroKernel.Registration;
using Castle.MicroKernel;
using Castle.Windsor;
using CluedIn.Core.Data.Serialization;
using CluedIn.Core;

namespace CluedIn.QualityAssurance.Cli.Services
{
    internal class CluedInSerializer
    {
        public static string SerializeToXml<T>(T obj)
            where T : ISerializable
        {
            var serializer = GetXmlSerializer();
            var xml = serializer.Serialize(obj);
            return xml;

        }

        public static string SerializeToJson<T>(T obj)
            where T : ISerializable
        {
            var serializer = GetJsonSerializer();
            var json = serializer.Serialize(obj);
            return json;
        }

        public static T DeserializeFromXml<T>(string xml)
            where T : ISerializable
        {
            var serializer = GetXmlSerializer();
            var obj = serializer.Deserialize<T>(xml);
            return obj;
        }

        public static T DeserializeFromJson<T>(string json)
            where T : ISerializable
        {

            var serializer = GetJsonSerializer();
            var obj = serializer.Deserialize<T>(json);
            return obj;
        }

        private static JsonSerializer GetJsonSerializer()
        {
            return new JsonSerializer(new ApplicationContext(new Dummy()), SerializationFlavor.Persisting);
        }

        private static XmlSerializer GetXmlSerializer()
        {
            return new XmlSerializer(SerializationFlavor.Persisting);
        }


        private class Dummy : IWindsorContainer
        {
            public IKernel Kernel => throw new NotImplementedException();

            public string Name => throw new NotImplementedException();

            public IWindsorContainer Parent { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public void AddChildContainer(IWindsorContainer childContainer)
            {
                throw new NotImplementedException();
            }

            public IWindsorContainer AddFacility(IFacility facility)
            {
                throw new NotImplementedException();
            }

            public IWindsorContainer AddFacility<TFacility>() where TFacility : IFacility, new()
            {
                throw new NotImplementedException();
            }

            public IWindsorContainer AddFacility<TFacility>(Action<TFacility> onCreate) where TFacility : IFacility, new()
            {
                throw new NotImplementedException();
            }

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public IWindsorContainer GetChildContainer(string name)
            {
                throw new NotImplementedException();
            }

            public IWindsorContainer Install(params IWindsorInstaller[] installers)
            {
                throw new NotImplementedException();
            }

            public IWindsorContainer Register(params IRegistration[] registrations)
            {
                throw new NotImplementedException();
            }

            public void Release(object instance)
            {
                throw new NotImplementedException();
            }

            public void RemoveChildContainer(IWindsorContainer childContainer)
            {
                throw new NotImplementedException();
            }

            public object Resolve(string key, Type service)
            {
                throw new NotImplementedException();
            }

            public object Resolve(Type service)
            {
                throw new NotImplementedException();
            }

            public object Resolve(Type service, Arguments arguments)
            {
                throw new NotImplementedException();
            }

            public T Resolve<T>()
            {
                throw new NotImplementedException();
            }

            public T Resolve<T>(Arguments arguments)
            {
                throw new NotImplementedException();
            }

            public T Resolve<T>(string key)
            {
                throw new NotImplementedException();
            }

            public T Resolve<T>(string key, Arguments arguments)
            {
                throw new NotImplementedException();
            }

            public object Resolve(string key, Type service, Arguments arguments)
            {
                throw new NotImplementedException();
            }

            public T[] ResolveAll<T>()
            {
                throw new NotImplementedException();
            }

            public Array ResolveAll(Type service)
            {
                throw new NotImplementedException();
            }

            public Array ResolveAll(Type service, Arguments arguments)
            {
                throw new NotImplementedException();
            }

            public T[] ResolveAll<T>(Arguments arguments)
            {
                throw new NotImplementedException();
            }
        }
    }
}
