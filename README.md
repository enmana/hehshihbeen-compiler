# HB(hehshihbeen)

**中文优先的「高级语言 → 中间层 → 汇编」工具链 + 中文 IDE。** 关键字、内建、报错、帮助一律中文(英文是别名);
自带 4 字节定长指令集;从源码到汇编,每一层都能展开给人看。

- **学语言 / 查用法**:[在线帮助站](https://enmana.github.io/hehshihbeen-compiler/)(整套中文文档都在那儿)
- **不装 .NET 直接用**:[Releases](https://github.com/enmana/hehshihbeen-compiler/releases) 下 `hbcc.exe`(自包含单文件,Windows x64)
- **看例子**:`Sample\` 下每个标准工程都有 `主程序.hb` 与一行 `// 预期输出:`
- **从源码自己编**:装 .NET 10 SDK,`dotnet build 编译器CLI\编译器CLI.csproj -c Release`(图形 IDE 用 `dotnet build hehshihbeen-compiler.csproj`)
