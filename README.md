# DotYouCore

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/dotnet.yml/badge.svg)


#### Setup

Notes:
* The API tests use frodobaggins.me and samwisegamgee.me.  Public DNS has these configured to point to 127.0.0.1

#### General Development standards: 
- Use Youverse.Core.Util.PathUtil.Combine when creating file and directory paths to ensure they work cross platform (use instead of Path.Combine)
