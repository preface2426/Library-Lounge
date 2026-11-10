# 首版房间素材准备

下载与校验日期：2026-10-29（北京时间）。来源：Kenney 官网。两包均已下载、解压，逐文件核对压缩包与解压内容，并检查包内 CC0 许可；其中家具包的键盘、鼠标、沙发 3 个模型用于当前客厅房间，旧独立桌椅已从工程删除；建筑包尚未导入。

| 素材包 | 包内版本 | 实际 FBX 文件数 | 主要用途 |
| --- | --- | --- | --- |
| Furniture Kit | 2.0 | 140 | 电脑桌、椅子、显示器外壳、键鼠、沙发和灯 |
| Building Kit | 1.0 | 79 | 墙、地面、门窗及可交互门的备用组件 |

数量来自实际下载包，表示文件数，不表示最终使用数量。来源地址、大小和 SHA-256 校验值见 [manifest.json](manifest.json)。

## 文件位置

- [家具包原始压缩文件](kenney_furniture-kit.zip)，解压目录为 `furniture-kit/`。
- [建筑包原始压缩文件](kenney_building-kit.zip)，解压目录为 `building-kit/`。
- 模型路径均为对应目录内的 `Models/FBX format/`；原包也保留其他格式和贴图，等待实际构建后清理。
- 包内许可：[家具包 License.txt](furniture-kit/License.txt)、[建筑包 License.txt](building-kit/License.txt)。
- `*.source-page.html` 为下载时保存的来源页面，不属于应用素材，无需导入工程。

## 首轮选用候选

Furniture Kit 优先查看 `desk`、`deskCorner`、`chairDesk`、`computerScreen`、`computerKeyboard`、`computerMouse`、`loungeDesignSofa` 及 `lampSquareCeiling`。

Building Kit 优先查看 `floor`、`wall-doorway-square`、`door-rotate-square-a` 等组件。简单墙体仍可直接使用 Unity 基础几何；最终用哪些以房间布局为准。

显示器模型只作为外壳；真实桌面所需的显示平面及交互由项目实现，不属于素材包已经提供的功能。选用的模型已完成 Unity 导入和运行包构建；已用独立运行包检查房间视角和基础状态。其余模型未做 Unity 验证，真实鼠标手感、输入法和长期性能仍待人工试用。

## 执行约定与后续清理

用户已创建 NetCafeClient 并授权基础玩法构建。当前工程只保留键盘、鼠标、沙发三个模型，未导入其余素材；默认教学内容与示例场景已清理。原始下载包在本目录保留作来源备份，不进入运行包。

房间构建完成后，检查实际场景、预制体、材质、模型和纹理引用，再删除多余物件、演示内容及重复格式。清理后重新构建验证，保留实际依赖、原始许可和来源记录；未使用的下载备份也在这一步统一整理。
