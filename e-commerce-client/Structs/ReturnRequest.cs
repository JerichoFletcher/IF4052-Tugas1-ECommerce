using Newtonsoft.Json;

namespace ECommerceClient.Structs;

public class ReturnRequest {
    [JsonProperty(PropertyName = "approved")]
    public bool Approved { get; set; }
}
