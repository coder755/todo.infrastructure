using Amazon.CDK;
using Infrastructure.Database;
using Infrastructure.FrontEnd;
using Infrastructure.PubSub;
using Infrastructure.Routing;
using Infrastructure.StorageService;
using Infrastructure.UserService;
using Infrastructure.Vpc;

namespace Infrastructure;
internal static class Program
{
    private const string Account = "442042533215";
    private const string Region = "us-east-1";

    public static void Main()
    {
        var app = new App();
        var env = new Environment
        {
            Account = Account,
            Region = Region
        };
        var props = new StackProps { Env = env };
        var vpcStack = new VpcStack(app, "TodoVpcStack", props);
        var frontEndStack = new FrontEndStack(app, "TodoFrontEndStack", props);
        var userServiceStack = new UserServiceStack(app, "TodoUsStack" , new UserServiceStackProps
        {
            Env = env,
            Vpc = vpcStack.Vpc,
            AvailabilityZones = vpcStack.AvailabilityZones
        });
        var storageServiceStack = new StorageServiceStack(app, "TodoStoreStack", new StorageServiceStackProps
        {
            Env = env,
            Vpc = vpcStack.Vpc,
            LoadBalancer = userServiceStack.LoadBalancer
        });
        var routingStack = new RoutingStack(app, "TodoRoutingStack", new RoutingStackProps
        {
            Env = env,
            LoadBalancer = userServiceStack.LoadBalancer,
            StorageLoadBalancer = storageServiceStack.LoadBalancer,
            Bucket = frontEndStack.Bucket,
        });
        var pubSubStack = new PubSubStack(app, "TodoPubSubStack", new PubSubStackProps
        {
            Env = env,
            Vpc = vpcStack.Vpc
        });
        var databaseStack = new DatabaseStack(app, "TodoDbStack", new DatabaseStackProps
        {          
            Env = env,
            Vpc = vpcStack.Vpc
        });
        
        app.Synth();
    }
}

