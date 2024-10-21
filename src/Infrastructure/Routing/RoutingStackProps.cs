using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.S3;

namespace Infrastructure.Routing;

public class RoutingStackProps : StackProps
{
    public ApplicationLoadBalancer LoadBalancer { get; set; }
    public ApplicationLoadBalancer StorageLoadBalancer { get; set; }
    public Bucket Bucket { get; set; }
}
