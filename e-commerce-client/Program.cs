using ECommerceClient.Structs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog.Extensions.Logging;
using Zeebe.Client;
using Zeebe.Client.Api.Responses;
using Zeebe.Client.Api.Worker;
using Zeebe.Client.Impl.Builder;

namespace ECommerceClient;

public class Program {
    private static IZeebeClient? client;
    private static readonly List<IJobWorker> workers = [];
    private static readonly ILoggerFactory loggerFactory = new NLogLoggerFactory();
    private static readonly ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

    public static async Task Main(string[] args) {
        IConfiguration config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();
        var camundaConfig = config.GetRequiredSection("Camunda");

        client = CamundaCloudClientBuilder.Builder()
            .UseClientId(camundaConfig["ClientId"])
            .UseClientSecret(camundaConfig["ClientSecret"])
            .UseContactPoint(camundaConfig["Endpoint"])
            .UseLoggerFactory(loggerFactory)
            .Build();

        using(client) {
            var topology = await client.TopologyRequest().Send();
            
            RegisterWorkers();
            while(Console.ReadKey(true).KeyChar != 'q') ;
            UnregisterWorkers();
        }
    }

    private static void RegisterWorkers() {
        workers.Add(
            client!.NewWorker()
                .JobType("PlaceOrder")
                .Handler(PlaceOrderHandler)
                .MaxJobsActive(3)
                .Timeout(TimeSpan.FromSeconds(10))
                .PollInterval(TimeSpan.FromSeconds(30))
                .PollingTimeout(TimeSpan.FromSeconds(10))
                .Name("PlaceOrderWorker")
                .Open()
        );

        workers.Add(
            client!.NewWorker()
                .JobType("PayOrder")
                .Handler(PayOrderHandler)
                .MaxJobsActive(3)
                .Timeout(TimeSpan.FromSeconds(10))
                .PollInterval(TimeSpan.FromSeconds(30))
                .PollingTimeout(TimeSpan.FromSeconds(10))
                .Name("PayOrderWorker")
                .Open()
        );

        workers.Add(
            client!.NewWorker()
                .JobType("ShipOrder")
                .Handler(PayOrderHandler)
                .MaxJobsActive(3)
                .Timeout(TimeSpan.FromSeconds(10))
                .PollInterval(TimeSpan.FromSeconds(30))
                .PollingTimeout(TimeSpan.FromSeconds(10))
                .Name("ShipOrderWorker")
                .Open()
        );
    }

    private static void UnregisterWorkers() {
        foreach(var worker in workers) {
            worker.Dispose();
        }
    }

    private static async Task PlaceOrderHandler(IJobClient jobClient, IJob job) {
        var item = JsonConvert.DeserializeObject<Dictionary<string, Item>>(job.Variables)["item"];
        logger.LogInformation("Item ID: {ItemId}, Variant: {ItemVariant}, Q: {ItemQuantity}", item.Id, item.Variant, item.Quantity);

        await client!.NewPublishMessageCommand()
            .MessageName("msgCustomerPlaceOrder")
            .CorrelationKey(item.Id.ToString())
            .Variables(job.Variables)
            .Send();

        await jobClient.NewCompleteJobCommand(job).Send();
    }

    private static async Task PayOrderHandler(IJobClient jobClient, IJob job) {
        var item = JsonConvert.DeserializeObject<Dictionary<string, Item>>(job.Variables)["item"];
        logger.LogInformation("Payment for item ID: {ItemId}", item.Id);

        await client!.NewPublishMessageCommand()
            .MessageName("msgCustomerPayOrder")
            .CorrelationKey(item.Id.ToString())
            .Send();

        await jobClient.NewCompleteJobCommand(job).Send();
    }

    private static async Task ShipOrderHandler(IJobClient jobClient, IJob job) {
        var item = JsonConvert.DeserializeObject<Dictionary<string, Item>>(job.Variables)["item"];
        logger.LogInformation("Send shipment status for item {ItemId}", item.Id);

        await client!.NewPublishMessageCommand()
            .MessageName("msgSellerShipOrder")
            .CorrelationKey(item.Id.ToString())
            .Send();

        await jobClient.NewCompleteJobCommand(job).Send();
    }
}
