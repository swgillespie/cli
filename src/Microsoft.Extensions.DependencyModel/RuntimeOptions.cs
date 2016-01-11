using Newtonsoft.Json.Linq;

namespace Microsoft.Extensions.DependencyModel
{
    public class RuntimeOptions
    {
        public JObject RawValues { get; }

        public RuntimeOptions(JObject rawValues)
        {
            RawValues = rawValues;
        }
    }
}
