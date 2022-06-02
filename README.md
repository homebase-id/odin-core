# DotYouCore

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/dotnet.yml/badge.svg)

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/ubuntu-build.yml/badge.svg)



#### Setup

Notes:
* The API tests use frodo.digital and samwise.digital.  Public DNS has these configured to point to 127.0.0.1
* For UI work, you should pull the repos for the react-client-apps and run npm start in each.  The web Youverse.Hosting project uses a proxy server to serve these in the dev env.
  * https://github.com/YouFoundation/dotyoucore-owner-console
  * https://github.com/YouFoundation/dotyoucore-public-app

#### General Development standards: 
- Use Youverse.Core.Util.PathUtil.Combine when creating file and directory paths to ensure they work cross platform (use instead of Path.Combine)



#### SSL Notes
Generate New CSR w/o requiring a passphase

`openssl req -new -newkey rsa:2048 -nodes -keyout private.key -out domain-name.csr`