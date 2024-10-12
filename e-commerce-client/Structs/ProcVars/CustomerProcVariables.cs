using Newtonsoft.Json;

namespace ECommerceClient.Structs.ProcVars;

public class CustomerProcVariables : IProcVars {
    [JsonProperty(PropertyName = "order")]
    public required Order Order { get; set; }

    [JsonProperty(PropertyName = "isDefect")]
    public string? IsDefect { get; set; }

    [JsonProperty(PropertyName = "returnRequest")]
    public ReturnRequest? ReturnRequest { get; set; }
}
