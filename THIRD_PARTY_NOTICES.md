# 第三方组件声明

鲨译的 PDF 文本提取使用 [PdfPig 0.1.8](https://www.nuget.org/packages/PdfPig/0.1.8)，由 UglyToad 项目提供，采用 Apache License 2.0。该组件的程序集嵌入在单个 Sharkey.exe 中，不会另行安装。许可证全文见 `third_party/PdfPig/LICENSE.txt`；试用包中对应 `PdfPig-APACHE-2.0.txt`。

此组件只读取 PDF 中已有的文本，不负责扫描件 OCR。扫描件可在外贸助手中截取相关页面并作为图片添加。

为了让 PdfPig 在较旧的 Windows .NET Framework 环境中也能以单个 EXE 运行，鲨译同时嵌入 [Microsoft System.ValueTuple 4.5.0](https://www.nuget.org/packages/System.ValueTuple/4.5.0)。其许可证见 `third_party/PdfPig/ValueTuple-LICENSE.txt`；试用包中对应 `ValueTuple-LICENSE.txt`。
