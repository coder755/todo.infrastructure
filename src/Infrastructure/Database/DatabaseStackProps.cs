using Amazon.CDK;
using Amazon.CDK.AWS.EC2;

namespace Infrastructure.Database;

public class DatabaseStackProps : StackProps
{
    public IVpc Vpc { get; set; }
}