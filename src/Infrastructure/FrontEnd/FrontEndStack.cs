using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SSM;
using Constructs;

namespace Infrastructure.FrontEnd;

public class FrontEndStack : Stack
{
    private const string BaseNamespace = "todo";
    private const string ServiceName = "frontEnd";
    
    public Bucket Bucket { get; set; }
    internal FrontEndStack(Construct scope, string stackId, IStackProps props) : base(scope, stackId, props)
    {
        const string serviceNamespace = BaseNamespace + "." + ServiceName;
        const string dashedServiceNamespace = BaseNamespace + "-" + ServiceName;
        
        const string bucketName = serviceNamespace + ".bucket";
        Bucket = new Bucket(this, bucketName, new BucketProps
        {
            BucketName = bucketName.ToLower(),
            RemovalPolicy = RemovalPolicy.DESTROY,
            PublicReadAccess = false,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
        });
        
        // setup Origin Access for Cloud Front
        var oai = new OriginAccessIdentity(this, serviceNamespace + ".oai", new OriginAccessIdentityProps
        {
            Comment = "Connects " + bucketName + " to " + stackId + " CDN",
        });
        Bucket.GrantRead(oai);
        
        var unused = new StringParameter(this, serviceNamespace + ".stringParameter.bucket.oai.id", new StringParameterProps
        {
            ParameterName = serviceNamespace + ".bucket.oai.id",
            StringValue = oai.OriginAccessIdentityId
        });
        
        var userPool = new UserPool(this, serviceNamespace + ".userpool", new UserPoolProps
        {
            UserPoolName = dashedServiceNamespace + "-user-pool",
            AutoVerify = new AutoVerifiedAttrs
            {
                Email = true,
                Phone = false
            },
            SelfSignUpEnabled = true,
            UserVerification = new UserVerificationConfig {
                EmailSubject = "Verify your email for Coder's Todo App!",
                EmailBody = "Thanks for signing up for Coder's Todo App! Your verification code is {####}",
                EmailStyle = VerificationEmailStyle.CODE,
                SmsMessage = "Thanks for signing up for Coder's Todo App! Your verification code is {####}"
            },
            SignInCaseSensitive = false,
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = RemovalPolicy.DESTROY,
            PasswordPolicy = new PasswordPolicy {
                MinLength = 6,
                RequireLowercase = false,
                RequireUppercase = false,
                RequireDigits = false,
                RequireSymbols = false
            }
        });
        
        var appClient = userPool.AddClient(dashedServiceNamespace + "-app-client", new UserPoolClientOptions
        {
            UserPoolClientName = dashedServiceNamespace + "-app-client",
            AuthFlows = new AuthFlow
            {
                UserPassword = true
            }
        });
    }
}