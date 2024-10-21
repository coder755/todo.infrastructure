using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;

namespace Infrastructure.StorageService;

public class StorageServiceStackProps : StackProps
{
    public IVpc Vpc { get; set; }
    public ApplicationLoadBalancer LoadBalancer { get; set; }
}