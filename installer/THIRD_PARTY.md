# Installer build tools

The installer uses WiX Toolset 4.0.6 and WixToolset.UI.wixext 4.0.6 from their
official NuGet packages. These are licensed under the Microsoft Reciprocal
License (MS-RL). Tool download hashes are pinned in build-installer.ps1.

Copyright (c) .NET Foundation and contributors. All rights reserved.

Source: https://github.com/wixtoolset/wix/tree/v4.0.6
License: https://github.com/wixtoolset/wix/blob/v4.0.6/LICENSE.TXT

The MSI contains the standard WiX dialog library and uses its unmodified build
components. The authored Lantoss.wxs and build script are included here.
The application runtime and dependency notices are retained at the package root.
