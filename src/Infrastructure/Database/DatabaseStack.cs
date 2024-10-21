using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.AWS.SSM;
using Constructs;
using Newtonsoft.Json;
using InstanceProps = Amazon.CDK.AWS.EC2.InstanceProps;
using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

namespace Infrastructure.Database;

public class DatabaseStack : Stack
{
    private const string BaseNamespace = "todo";
    private const string ServiceName = "database";
    internal DatabaseStack(Construct scope, string stackId, DatabaseStackProps props) : base(scope, stackId,
        props)
    {
        const string serviceNamespace = BaseNamespace + "." + ServiceName;
        const string dashedServiceNamespace = BaseNamespace + "-" + ServiceName;

        // set up bastion host
        // Create a security group to allow SSH only from my IP
        var bastionSecurityGroup = new SecurityGroup(this, serviceNamespace + ".bastion.sg", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            AllowAllOutbound = true,
            Description = "Allow SSH access from my IP",
        });
        const string myIp = "73.153.180.9/32";
        bastionSecurityGroup.AddIngressRule(Peer.Ipv4(myIp), Port.Tcp(22), "Allow SSH from my IP");
        
        var bastion = new Instance_(this, serviceNamespace + ".bastion", new InstanceProps
        {
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
            InstanceType = InstanceType.Of(InstanceClass.T3, InstanceSize.MICRO),
            MachineImage = MachineImage.LatestAmazonLinux(),
            KeyName = "coderBastion",
            SecurityGroup = bastionSecurityGroup,
        });

        // set up database
        var dbSecret = new Secret(this, serviceNamespace + ".secrete.db", new SecretProps
        {
            SecretName = BaseNamespace + ServiceName.FirstCharToUpper() + "Secret",
            Description = "Credentials to the RDS instance",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = JsonConvert.SerializeObject(new Dictionary<string, string> { { "username", "coder" } }),
                GenerateStringKey = "password",
                ExcludePunctuation = true,
                ExcludeCharacters = "@/\\\" "
            }
        });
        
        var dbSecurityGroup = new SecurityGroup(this, serviceNamespace + ".securityGroup.db", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            SecurityGroupName = dashedServiceNamespace + "-db-securityGroup",
            AllowAllOutbound = true
        });
        var storageFargateSgId = StringParameter.ValueFromLookup(this, "todo.storageService.fargate.sg");
        var storageFargateSg = SecurityGroup.FromSecurityGroupId(this, serviceNamespace + ".fargate.sg", storageFargateSgId);
        dbSecurityGroup.AddIngressRule(storageFargateSg, Port.Tcp(3306), "Allow Storage Fargate to access db");
        dbSecurityGroup.AddIngressRule(bastionSecurityGroup, Port.AllTcp(), "Allow bastion to access db");
        
        var dbEngine = DatabaseInstanceEngine.Mysql(new MySqlInstanceEngineProps
        {
            Version = MysqlEngineVersion.VER_8_0_32
        });
        
        var database = new DatabaseInstance(this, serviceNamespace + ".db", new DatabaseInstanceProps
        {
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            InstanceType = InstanceType.Of(InstanceClass.T3, InstanceSize.MICRO),
            AllocatedStorage = 20,
            Engine = dbEngine,
            InstanceIdentifier = dashedServiceNamespace + "-db",
            Credentials = Credentials.FromSecret(dbSecret),
            SecurityGroups = new ISecurityGroup[] { dbSecurityGroup },
            RemovalPolicy = RemovalPolicy.DESTROY,
            PubliclyAccessible = false,
        });
    }
}