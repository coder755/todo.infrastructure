using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Amazon.CDK.AWS.SSM;
using Constructs;
using HealthCheck = Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck;
using Protocol = Amazon.CDK.AWS.EC2.Protocol;

namespace Infrastructure.UserService;

public class UserServiceStack : Stack
{
    public ApplicationLoadBalancer LoadBalancer { get; set; }
    
    private const string BaseNamespace = "todo";
    private const string ServiceName = "us";
    private const string CloudFrontPrefixListId = "pl-3b927c52";
    private const string SnsListenerApi = "api/notification/v1/sns-listener";

    internal UserServiceStack(Construct scope, string stackId, UserServiceStackProps props) : base(scope, stackId, props)
    {
        const string serviceNamespace = BaseNamespace + "." + ServiceName;
        const string dashedServiceNamespace = BaseNamespace + "-" + ServiceName;

        var cloudFrontPrefixList = Peer.PrefixList(CloudFrontPrefixListId);
        var snsVpcEndpointId = StringParameter.ValueFromLookup(this, "todo.vpc.endpoint.sns.id");
        var snsInterfaceEndpoint = InterfaceVpcEndpoint.FromInterfaceVpcEndpointAttributes(this,
            serviceNamespace + ".vpc.endpoint.sns", new InterfaceVpcEndpointAttributes
            {
                VpcEndpointId = snsVpcEndpointId,
                Port = 443
            });
        
        var httpSecurityGroup = new SecurityGroup(this, serviceNamespace + ".alb.sg.http", new SecurityGroupProps
        {
            AllowAllOutbound = true,
            SecurityGroupName = serviceNamespace + ".alb.securityGroup.http",
            Vpc = props.Vpc,
        });
        httpSecurityGroup.AddIngressRule(cloudFrontPrefixList, new Port(new PortProps
        {
            FromPort = 80, ToPort = 80, Protocol = Protocol.TCP, StringRepresentation = "80:80:TCP"
        }));
        var httpsSecurityGroup = new SecurityGroup(this, serviceNamespace + ".alb.sg.https", new SecurityGroupProps
        {
            AllowAllOutbound = true,
            SecurityGroupName = serviceNamespace + ".alb.securityGroup.https",
            Vpc = props.Vpc
        });
        httpsSecurityGroup.AddIngressRule(cloudFrontPrefixList, new Port(new PortProps
        {
            FromPort = 443, ToPort = 443, Protocol = Protocol.TCP, StringRepresentation = "443:443:TCP"
        }));
        httpsSecurityGroup.Connections.AllowFrom(snsInterfaceEndpoint, Port.Tcp(443));
        
        LoadBalancer = new ApplicationLoadBalancer(this, serviceNamespace + ".alb", new ApplicationLoadBalancerProps
        {
            Vpc = props.Vpc,
            InternetFacing = true,
            VpcSubnets = new SubnetSelection {
                SubnetType = SubnetType.PUBLIC,
                AvailabilityZones = props.AvailabilityZones
            },
            IpAddressType = IpAddressType.IPV4,
            LoadBalancerName = dashedServiceNamespace + "-alb",
            SecurityGroup = httpSecurityGroup
        });
        LoadBalancer.AddSecurityGroup(httpsSecurityGroup);
        
        // create ECS cluster
        var cluster = new Cluster(this, serviceNamespace + ".cluster", new ClusterProps
        {
            ClusterName = dashedServiceNamespace + "-cluster",
            Vpc = props.Vpc, 
            EnableFargateCapacityProviders = true,
        });
        
        var targetGroup = new ApplicationTargetGroup(this, serviceNamespace + ".targetGroup", new ApplicationTargetGroupProps
        {
            Protocol = ApplicationProtocol.HTTP,
            Port = 80,
            HealthCheck = new HealthCheck()
            {
                Path = "/healthcheck"
            },
            TargetType = TargetType.IP,
            Vpc = props.Vpc,
            TargetGroupName = dashedServiceNamespace
        });
        
        LoadBalancer.AddListener(serviceNamespace + ".alb.http.listener", new ApplicationListenerProps
        {
            Protocol = ApplicationProtocol.HTTP,
            Port = 80,
            DefaultTargetGroups = new [] { targetGroup },
        });
        
        var certificateArn = StringParameter.ValueFromLookup(this, "todo.routing.stringParameter.certificate.arn");
        var certificate = ListenerCertificate.FromArn(certificateArn);
        LoadBalancer.AddListener(serviceNamespace + ".alb.https.listener", new ApplicationListenerProps
        {
            Protocol = ApplicationProtocol.HTTPS,
            Port = 443,
            SslPolicy = SslPolicy.RECOMMENDED,
            Certificates = new IListenerCertificate[]{certificate},
            DefaultAction = ListenerAction.Forward(new []{targetGroup})
        });
        
        var ecsRole = new Role(this, serviceNamespace + ".ecsRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com")
        });
        
        ecsRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new []{
                "ecr:GetAuthorizationToken",
                "ecr:BatchCheckLayerAvailability",
                "ecr:GetDownloadUrlForLayer",
                "ecr:BatchGetImage",
                "logs:CreateLogStream",
                "logs:PutLogEvents"
                },
            Resources = new []{"*"}
        }));
        
        var taskRole = new Role(this, serviceNamespace + ".taskRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new []
            {
                ManagedPolicy.FromAwsManagedPolicyName("AmazonSQSFullAccess")
            }
        });
        
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:GetResourcePolicy",
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret",
                "secretsmanager:ListSecretVersionIds",
                "secretsmanager:ListSecrets",
                "sns:Publish"
            },
            Resources = new[] { "*" }
        }));
        
        // create ECR
        var ecrRepository = new Repository(this, serviceNamespace + ".ecr", new RepositoryProps
        {
            RepositoryName = serviceNamespace, RemovalPolicy = RemovalPolicy.DESTROY
        } );
        
        // create the task definition
        var taskDefinition = new TaskDefinition(this, serviceNamespace + ".taskDefinition", new TaskDefinitionProps {
            Compatibility = Compatibility.FARGATE,
            Family = dashedServiceNamespace,
            Cpu = "256",
            MemoryMiB = "512",
            RuntimePlatform = new RuntimePlatform
            {
                OperatingSystemFamily = OperatingSystemFamily.LINUX,
                CpuArchitecture = CpuArchitecture.X86_64,
            },
            ExecutionRole =  ecsRole,
            TaskRole = taskRole
        });
        var toProcessQueueUrl = StringParameter.ValueFromLookup(this, "todo.pubsub.toprocess.queue.url");
        var snsTopicArn = StringParameter.ValueFromLookup(this, "todo.pubsub.sns.topic.arn");
        taskDefinition.AddContainer(serviceNamespace + ".container", new ContainerDefinitionOptions
        {
            ContainerName = dashedServiceNamespace.ToLower() + "-container",
            Image = ContainerImage.FromEcrRepository(ecrRepository, "todo.users"),
            PortMappings = new IPortMapping[] { new PortMapping
            {
                Name = dashedServiceNamespace + "-container-port-mapping",
                ContainerPort = 80,
                Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                AppProtocol = AppProtocol.Http
            } },
            Environment = new Dictionary<string, string>
            {
                {"TO_PROC_QUEUE_URL", toProcessQueueUrl},
                {"TOPIC_ARN", snsTopicArn}
            },
            Logging = new AwsLogDriver(new AwsLogDriverProps
            {
                StreamPrefix = dashedServiceNamespace + "-container",
                LogGroup = new LogGroup(this, serviceNamespace + ".container.log.groups", new LogGroupProps
                {
                    LogGroupName = dashedServiceNamespace + "-container"
                })
            })
        });
        
        // set up the Fargate Service
        var fargateSecGroup = new SecurityGroup(this, serviceNamespace + ".fargateService.securityGroup", new SecurityGroupProps
        {
            AllowAllOutbound = true,
            SecurityGroupName = serviceNamespace + ".fargateService.securityGroup",
            Vpc = props.Vpc
        });
        fargateSecGroup.AddIngressRule(httpsSecurityGroup, Port.AllTcp(), "Allow traffic from ALB to Fargate on all ports");
        
        var unused2 = new StringParameter(this, serviceNamespace + ".stringParameter.fargate.sg", new StringParameterProps
        {
            ParameterName = serviceNamespace + ".fargate.sg",
            StringValue = fargateSecGroup.SecurityGroupId
        });
        
        var fargateService = new FargateService(this, serviceNamespace + ".fargateService", new FargateServiceProps
        {
            Cluster   = cluster,
            TaskDefinition = taskDefinition,
            AssignPublicIp = false,
            ServiceName = dashedServiceNamespace + "-fargate-service",
            VpcSubnets = new SubnetSelection {
                SubnetType = SubnetType.PRIVATE_WITH_EGRESS
            },
            SecurityGroups = new ISecurityGroup[] {fargateSecGroup},
            DesiredCount = 1,
        });
        fargateService.AttachToApplicationTargetGroup(targetGroup);
        
        Console.Write($"topic arn: {snsTopicArn}");
        
        var snsTopic = Topic.FromTopicArn(this, serviceNamespace + ".sns.topic", snsTopicArn);
        var snsNotificationUrl = $"https://{LoadBalancer.LoadBalancerDnsName}/{SnsListenerApi}";
        // snsTopic.AddSubscription(new UrlSubscription(snsNotificationUrl, new UrlSubscriptionProps
        // {
        //     Protocol = SubscriptionProtocol.HTTPS,
        // }));
    }
}

