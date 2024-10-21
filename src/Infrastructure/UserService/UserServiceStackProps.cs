using Amazon.CDK;
using Amazon.CDK.AWS.EC2;

namespace Infrastructure.UserService;

public class UserServiceStackProps : StackProps
{
    public IVpc Vpc { get; set; }
    public string[] AvailabilityZones { get; set; }
}