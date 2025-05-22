# login

<!-- BEGIN USAGE -->

### Description

| Name | Description |
| ---- | ----------- |

Login into one or more package respositories

## Usage

```
cmf login [<repositoryType> [<repository>]] [command] [options]
```

### Arguments

| Name           | Description                 |
| -------------- | --------------------------- | 
| `<CIFS|Docker|NPM|NuGet|Portal>` | Type of repository for login (values: portal, docker, npm, nuget, cifs) |
| `<repository>` | URL of repository for login |

### Options

| Name                        | Description                                                                                                                                            |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `-T, --auth-type <Basic|Bearer>` | Type of authentication type to use (only needed if the repository type supports more than one) |
| `-t, --token <token>`       | Token used for this, used when the auth type is Bearer                                                                                                 |
| `-u, --username <username>` | Account username, used when the auth type is Basic                                                                                                     |
| `-p, --password <password>` | Account password, used when the auth type is Basic                                                                                                     |
| `-d, --domain <domain>`     | For repositories that support it, the domain to use when logging in.                                                                                   |
| `-k, --key <key>`           | For repositories that support it, the key under which we should store the credential                                                                   |
| `--store-only`              | If true, the credentials are stored on the `.cmf-auth.json` file, but are not applied to the credentials file of the tool (NPM, NuGet, Docker, etc...) |
| `--no-prompt`               | Do not display any interactive prompts. If a prompt was needed, an error will be raised instead                                                        |
| `-?, -h, --help`            | Show help and usage information                                                                                                                        |

### Commands

| Name   | Description                                                                                                             |
| ------ | ----------------------------------------------------------------------------------------------------------------------- |
| `sync` | Sync credentials from the .cmf-auth.json file into each specific tool (npm, nuget, docker, etc...) configuration files. |

<!-- END USAGE -->

## Overview

The most common use case for this command is to run only:

```shell
cmf login
```

This will automatically authenticate into [Critical Manufacturing's Customer Portal](https://portal.criticalmanufacturing.com) (if you are already logged into it on your browser), and also authenticate using the same credentials to the [official registries](https://developer.criticalmanufacturing.com/support/faqs/cm-public-repository/) (NPM, Nuget and Docker) from CM.

Broadly speaking, the goal of this command is to manage the credentials needed by all other `cmf` commands. Most commonly, this is related to:

- Logging in to Critical Manufacturing's Customer Portal, to be able to access licenses and related information.
- Logging in to the NPM, NuGet and Docker registries where development dependencies are stored (allowing both official CM repositories or third-party ones).

To support logging into alternative registries (that can serve third-party packages or serve as self-hosted proxy registries to CM's official ones), you can also use the same command, but you will need to pass it some more arguments, depending on the type of registry you want to authenticate.

```shell
# NPM
cmf login npm <registry> --auth-type Basic -u <username> -p <password>
# NuGet
cmf login nuget <registry> --auth-type Basic -k CMF -u <username> -p <password>
# Docker
cmf login docker <registry> --auth-type Basic -u <username> -p <password>
# CM Portal (with a specific token)
cmf login portal --auth-type Bearer -t <token>
```

!!! tip "Interactive Credentials Input"

    You can avoid the parameters `-u`/`--username`, `-p`/`--password` and `-t`/`--token`, in which case they will
    be prompted interactively on the console.

### Environment Variables

It is also possible to override some credentials when running commands through the use of environment variables. The format of the environment variables is as follows:

```
<repoType>__<repoUrl>__<property>
```

| Token      | Description                                                                                                                                             |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `repoType` | Can be `npm` or `cifs`                                                                                                                                  |
| `repoUrl`  | The URL of the repository that this credential applies to. Must follow the encoding rules specified below. Examples: <ul><li>**NPM** `https://criticalmanufacturing.io/repository/npm/` => `criticalmanufacturing_io_repository_npm`</li><li>**CIFS** `\\hostname\share\sub\folder` => `hostname_share`</li></ul> |
| `property` | Can be `AUTHTYPE` (mandatory), `USERNAME`, `PASSWORD`, `TOKEN`, `KEY` and `DOMAIN`.                                                                                 |

!!! warning "Usage with External Tools"

    Credentials specified through environment variables are only used by `cmf` commands that interact directly with CIFS and NPM repositories.

    On the other hand, credentials supplied through `cmf login` will work fine even if you execute

#### Repository URL Encoding Rules
When specifying a Repository URL in an environment variable to override its credentials, the URl must be encoded following these rules:

- The initial protocol should be removed (ex `https://`)
- The final `/` must be removed 
- Any other occurrences of one of the following characters `- . /` must be replaced with an underscore `_`.
- For **CIFS** shares, should include only the host name and the share name (first path segment).

### Cmf Auth Fiile
The credentials are stored on the auth file

TDB