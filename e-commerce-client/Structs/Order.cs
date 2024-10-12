using Newtonsoft.Json;

namespace ECommerceClient.Structs;

public class Order {
    [JsonProperty(PropertyName = "id")]
    public int? Id { get; set; }

    [JsonProperty(PropertyName = "item")]
    public required OrderItem Item { get; set; }
}

public class OrderItem {
    [JsonProperty(PropertyName = "id")]
    public int Id { get; set; }
    [JsonProperty(PropertyName = "variantId")]
    public int VariantId { get; set; }
    [JsonProperty(PropertyName = "quantity")]
    public int Quantity { get; set; }
}
