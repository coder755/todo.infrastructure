using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Amazon.CDK.AWS.SSM;
using Constructs;

namespace Infrastructure.Routing;

public class RoutingStack : Stack
{
    private const string BaseNamespace = "todo";
    private const string ServiceName = "routing";
    private const string TodoDomainName = "huckandrose.com";
    private const string NotificationApi = "api/notification";

    internal RoutingStack(Construct scope, string stackId, RoutingStackProps props) : base(scope, stackId, props)
    {
        const string serviceNamespace = BaseNamespace + "." + ServiceName;

        var todoHostedZone = new HostedZone(this, serviceNamespace + ".Hostedzone", new HostedZoneProps
        {
            ZoneName = TodoDomainName,
        });
        
        var certificate = new Certificate(this, serviceNamespace + ".certificate", new CertificateProps {
            DomainName = TodoDomainName,
            CertificateName = "todoCertificate",  // Optionally provide an certificate name
            Validation = CertificateValidation.FromDns(todoHostedZone)
        });
        
        var unused = new StringParameter(this, serviceNamespace + ".stringParameter.certificate.arn", new StringParameterProps
        {
            ParameterName = serviceNamespace + ".stringParameter.certificate.arn",
            StringValue = certificate.CertificateArn
        });
        
        // Behaviors
        var userBehaviorOptions = new BehaviorOptions
        {
            Origin = new LoadBalancerV2Origin(props.LoadBalancer),
            ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
            AllowedMethods = AllowedMethods.ALLOW_ALL,
            CachePolicy = CachePolicy.CACHING_DISABLED,
            OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER
        };
        
        var todoBehaviorOptions = new BehaviorOptions
        {
            Origin = new LoadBalancerV2Origin(props.LoadBalancer),
            ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
            AllowedMethods = AllowedMethods.ALLOW_ALL,
            CachePolicy = CachePolicy.CACHING_DISABLED,
            OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER
        };
        
        var oaiId = StringParameter.ValueFromLookup(this, "todo.frontEnd.bucket.oai.id");

        var todoDistribution = new Distribution(this, serviceNamespace + ".CloudFront.Distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = new S3Origin(props.Bucket, new S3OriginProps
                {
                    OriginAccessIdentity = OriginAccessIdentity.FromOriginAccessIdentityId(this, serviceNamespace + ".bucket.oai", oaiId)
                }),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/api/user/*"] = userBehaviorOptions,
                [$"/{NotificationApi}/*"] = userBehaviorOptions,
                ["/api/todo/*"] = todoBehaviorOptions,
            },
            DefaultRootObject = "index.html",
            Certificate = certificate,
            DomainNames = new []{TodoDomainName},
            EnableIpv6 = false,
            ErrorResponses = new IErrorResponse[]
            {
                new ErrorResponse
                {
                    HttpStatus = 404,
                    ResponsePagePath = "/index.html", // Redirect to index.html for 404 errors
                    ResponseHttpStatus = 200,
                }
            }
        });

        // A record for distribution
        var todoARecord = new ARecord(this, serviceNamespace + ".aRecord", new ARecordProps
        {
            Zone = todoHostedZone,
            Target = RecordTarget.FromAlias(new CloudFrontTarget(todoDistribution))
        });
    }
}