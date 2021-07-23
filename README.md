# Crossmarc.Media.AWS.S3
AWS S3 Media Storage Module for [OrchardCore](https://github.com/OrchardCMS/OrchardCore)

## Status

### 0.5

This module has been upgraded to work with OrchardCore 1.0 and is currently being used for two OrchardCore CMS based web sites.

## Configuration

Standard .NET Core configuration can be used to set the AWS S3 bucket to use for content storage:

```
"OrchardCore": {
    "Crossmarc.Media.AWS.S3": {
      "BucketName": "oc-media-us-east-1-12345678901",
      "Prefix": "prod-1"
    }
  }
```

## Permissions

Ensure that the host environment for OrchardCore has permissions to the AWS S3 bucket. For example, if you are running in an EC2 instance or ECS task that has an assigned IAM role, ensure the role has permissions to read and write from the S3 bucket. An example permissions policy is shown below, but ensure you adjust it for your bucket name and prefix requirements.

```
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Sid": "AllowS3ReadWrite",
            "Effect": "Allow",
            "Action": [
                "s3:Get*",
                "s3:Put*",
                "s3:List*",
                "s3:Delete*"
            ],
            "Resource": [
                "arn:aws:s3:::oc-media-us-east-1-12345678901/*"
            ]
        }
    ]
}
```

## Code of Conduct

See [CODE-OF-CONDUCT](./CODE-OF-CONDUCT.md)

## Credits

This module is based on and inspired by the OrchardCore.Media.Azure module that is distributed as part of OrchardCore. Thanks to the OrchardCore contributors for supporting media storage extensibility. Thanks especially to @deanmarcussen, @sebastienros, and @jtkech for their [guidance](https://github.com/OrchardCMS/OrchardCore/issues/4107) along the way.

## Disclaimers

This module has not been reviewed or endorsed by the OrchardCore team or AWS. The work is my own.
