using ECommerceClient.Structs;
using ECommerceClient.Structs.ProcVars;
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
    private static Random random = new();

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
                .JobType("GenerateOrder")
                .Handler(GenerateOrderHandler)
                .MaxJobsActive(3)
                .Timeout(TimeSpan.FromSeconds(10))
                .PollInterval(TimeSpan.FromSeconds(30))
                .PollingTimeout(TimeSpan.FromSeconds(10))
                .Name("GenerateOrderWorker")
                .Open()
        );

        workers.Add(MessageThrowJob<CustomerProcVariables>("PlaceOrder", "msgCustomerPlaceOrder",
            vars => vars.Order.Id!.ToString()!,
            sentVariables: JsonConvert.SerializeObject,
            preCall: vars => logger.LogInformation("Place order for item ID: {ItemId}, Variant ID: {VariantId}, Qty: {Quantity}",
                vars.Order.Item.Id, vars.Order.Item.VariantId, vars.Order.Item.Quantity
            )
        ));
        workers.Add(MessageThrowJob<CustomerProcVariables>("PayOrder", "msgCustomerPayOrder",
            vars => vars.Order.Id!.ToString()!,
            preCall: vars => logger.LogInformation("Payment for order ID: {OrderId}", vars.Order.Id)
        ));
        workers.Add(MessageThrowJob<CustomerProcVariables>("OrderCompletion", "msgCustomerConfirmComplete",
            vars => vars.Order.Id!.ToString()!,
            preCall: vars => logger.LogInformation("Order completion confirmed for order ID: {OrderId}", vars.Order.Id)
        ));
        workers.Add(MessageThrowJob<CustomerProcVariables>("ReturnRequest", "msgCustomerRequestReturn",
            vars => vars.Order.Id!.ToString()!,
            preCall: vars => logger.LogInformation("Return requested for order ID: {OrderId}", vars.Order.Id)
        ));
        workers.Add(MessageThrowJob<CustomerProcVariables>("ShipReturn", "msgCustomerShipReturn",
            vars => vars.Order.Id!.ToString()!,
            preCall: vars => logger.LogInformation("Sent back returned item for order ID: {OrderId}", vars.Order.Id)
        ));
        workers.Add(MessageThrowJob<SellerProcVariables>("ShipOrder", "msgSellerShipOrder",
            vars => vars.Order.Id!.ToString()!,
            preCall: vars => logger.LogInformation("Sent shipment status for order ID: {OrderId}", vars.Order.Id)
        ));
        workers.Add(MessageThrowJob<SellerProcVariables>("ReturnApproval", "msgSellerReturnApproval",
            vars => vars.Order.Id!.ToString()!,
            sentVariables: JsonConvert.SerializeObject,
            preCall: vars => logger.LogInformation("Return request {Approval} for order ID: {OrderId}",
                (vars.ReturnRequest?.Approved ?? false) ? "approved" : "rejected", vars.Order.Id
            )
        ));
    }

    private static void UnregisterWorkers() {
        foreach(var worker in workers) {
            worker.Dispose();
        }
    }

    private static IJobWorker MessageThrowJob<T>(string jobType, string messageName, Func<T, string> correlationKey,
            Func<T, string>? sentVariables = null,
            Action<T>? preCall = null,
            Action<T>? postCall = null
        )
        where T : IProcVars {
        async Task publishMessageAction(IJob job) {
            var vars = JsonConvert.DeserializeObject<T>(job.Variables);
            var corrKey = correlationKey(vars);

            preCall?.Invoke(vars);
            var msgCommand = client!.NewPublishMessageCommand()
                .MessageName(messageName)
                .CorrelationKey(corrKey);
            if(sentVariables != null) {
                msgCommand = msgCommand.Variables(sentVariables(vars));
            }
            await msgCommand.Send();

            postCall?.Invoke(vars);
        }

        async Task jobHandler(IJobClient jobClient, IJob job) {
            await publishMessageAction(job).ContinueWith(async task => {
                if(task.IsFaulted) {
                    await jobClient.NewFailCommand(job.Key)
                        .Retries(job.Retries - 1)
                        .SendWithRetry();
                } else {
                    await jobClient.NewCompleteJobCommand(job).Send();
                }
            });
        }

        return client!.NewWorker()
            .JobType(jobType)
            .Handler(jobHandler)
            .MaxJobsActive(3)
            .Timeout(TimeSpan.FromSeconds(10))
            .PollInterval(TimeSpan.FromSeconds(30))
            .PollingTimeout(TimeSpan.FromSeconds(10))
            .Name($"{jobType}Worker")
            .Open();
    }

    private static async Task GenerateOrderHandler(IJobClient jobClient, IJob job) {
        var vars = JsonConvert.DeserializeObject<CustomerProcVariables>(job.Variables);
        var order = vars.Order;

        order.Id = random.Next(0, int.MaxValue);
        logger.LogInformation("Generated order ID {OrderId} for order with item ID {ItemId}, Variant ID {VariantId}, Qty {Quantity}",
            order.Id, order.Item.Id, order.Item.VariantId, order.Item.Quantity
        );

        await jobClient.NewCompleteJobCommand(job).Variables(JsonConvert.SerializeObject(vars)).Send();
    }
}
