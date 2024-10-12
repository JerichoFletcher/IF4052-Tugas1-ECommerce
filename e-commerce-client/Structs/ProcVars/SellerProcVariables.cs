using Newtonsoft.Json;

namespace ECommerceClient.Structs.ProcVars;

public class SellerProcVariables : IProcVars {
    [JsonProperty(PropertyName = "order")]
    public required Order Order { get; set; }

    [JsonProperty(PropertyName = "returnRequest")]
    public ReturnRequest? ReturnRequest { get; set; }
}
