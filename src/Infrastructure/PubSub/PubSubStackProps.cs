using Amazon.CDK;
using Amazon.CDK.AWS.EC2;

namespace Infrastructure.PubSub;

public class PubSubStackProps : StackProps
{
    public IVpc Vpc { get; set; }
}
