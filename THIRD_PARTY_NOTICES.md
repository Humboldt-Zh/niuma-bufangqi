# 第三方声明

本项目原创代码与素材使用根目录 MIT 许可证。第三方依赖及运行时保留各自许可；本文件不将它们改为 MIT。

## Microsoft WebView2 SDK

组件：`Microsoft.Web.WebView2`，版本 `1.0.4314.35`，作者 Microsoft。来源为 [NuGet 官方包](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35)。

已读取该版本 nupkg 内的 `LICENSE.txt` 和 `NOTICE.txt`。SDK 的许可为随包 LICENSE.txt 中的 BSD 三条款式文本；下文保留原文。NOTICE.txt 还列出其所含的 ANTLR3.Runtime 与 StringTemplate4 声明，亦原样保留。发布包应保留本文件和依赖自带的声明。

### LICENSE.txt 原文

```text
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors 
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### NOTICE.txt 原文

```text
NOTICES AND INFORMATION
Do Not Translate or Localize

This software incorporates material from third parties. Microsoft makes certain
open source code available at https://3rdpartysource.microsoft.com, or you may
send a check or money order for US $5.00, including the product name, the open
source component name, and version number, to:

Source Code Compliance Team
Microsoft Corporation
One Microsoft Way
Redmond, WA 98052
USA

Notwithstanding any other terms, you may reverse engineer this software to the
extent required to debug changes to any libraries licensed under the GNU Lesser
General Public License.

----------------------------------------------------------------

Antlr3.Runtime 3.5.2-rc1 - BSD 3-Clause

[The "BSD license"]
Copyright (c) 2011 The ANTLR Project
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

 1. Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
 2. Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.
 3. Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

---------------------------------------------------------

---------------------------------------------------------

StringTemplate4 4.0.9-rc1 - BSD 3-Clause

[The "BSD license"]
Copyright (c) 2011 The ANTLR Project
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

 1. Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
 2. Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.
 3. Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

---------------------------------------------------------
```

## WebView2 Runtime

Microsoft Edge WebView2 Evergreen Runtime 是独立于上述 NuGet SDK 的 Microsoft 运行时，按其自身许可和分发条款安装、维护及更新。本项目首版使用已安装的 Evergreen Runtime，不把它改为项目 MIT 许可，也不将整个浏览器运行时打入 ZIP。

缺失运行时的用户由应用引导到 [Microsoft 官方下载页](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)。需要将来改为捆绑或固定版本分发时，应重新核对当时的 [官方分发说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) 及所用安装器附带条款。

## .NET 运行时

自包含 win-x64 发布包含 .NET 运行依赖。发布包必须保留这些依赖随附的 LICENSE 与第三方声明，本文件不能代替运行时自己的声明。运行时源码及许可可查阅 [dotnet/runtime](https://github.com/dotnet/runtime)。

## 原创素材与参考资料

项目内原创角色、纸团 SVG、应用图标及原创改写文案由项目贡献者提供，使用项目 MIT 许可。系统字体由 Windows 或浏览器提供，本项目不重新分发字体文件。

产品调研链接仅作为资料来源，不表示复用了相关项目的代码或宠物素材。用户提供的调研原件及其私有提取文件不随源代码或软件发布；公开文案为项目原创改写，采用说明见 [话术记录](docs/report-copy-adaptation.md)。后续引入其他素材或代码时，贡献者必须另行记录其来源、版本、授权与署名要求。
