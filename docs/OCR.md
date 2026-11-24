# OCR 模型与 OnnxOCR 0.0.1 对齐

WW 继续使用 C# / ONNX Runtime，不在生产运行中启动 Python 或调用 OKWW。比较基准是 OKWW v3.8.4 的 `requirements.txt` 锁定的 `onnxocr-ppocrv6==0.0.1`（[PyPI 发布页](https://pypi.org/project/onnxocr-ppocrv6/0.0.1/)）。该 wheel 的 SHA-256 为 `0574fe622a0be6a1017bb04c3941ed5efd6740fd2388a6b8041a5acee882678d`，发布来源为 [OnnxOCR 提交 6e266b8](https://github.com/ok-oldking/OnnxOCR/tree/6e266b8dae2109fc4e00fb527283f96197fc64b3)。实际取得并核对该发布包，不以其他版本的默认参数代替。

## 资源一致性

`assets/ocr` 中的三份资源与 wheel 中 `onnxocr/models/ppocrv6/small/` 对应文件逐字节一致，不需要替换权重或字典。`manifest.json`、`LICENSE.txt` 来自已核验的上游包，供追溯模型来源和 Apache-2.0 许可。

| 文件 | SHA-256 |
|---|---|
| `det.onnx` | `d73e0058b7a8086bbd57f3d10b8bcd4ff95363f67e06e2762b5e814fe9c9410e` |
| `rec.onnx` | `5435fd747c9e0efe15a96d0b378d5bd157e9492ed8fd80edf08f30d02fa24634` |
| `ppocrv6_dict.txt` | `b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d` |

## 处理流程

| 上游锁定版本模块 | WW 实现 | 对齐范围 |
|---|---|---|
| `operators.py`, `predict_det.py`, `utils.py` | `OcrReader.PrepareDet` | BGR、黑色小图填充、最大960边、32对齐、双线性缩放、归一化、CHW；保持原始未填充尺寸用于输出框投影。 |
| `db_postprocess.py`, `predict_det.py` | `OcrDbPostprocess`, `OcrDetectionBox` | 二值图轮廓、候选上限、旋转最小矩形与得分、圆角扩边、实际模型输出形状投影、四点排序、边界及最小尺寸过滤；得分在局部 ROI 内先转换整点，按裁剪后的栅格边界与 16 位定点扫描线填充凸四边形，避免画面边缘斜框的漏计/误计。 |
| `utils.py` | `OcrRecognition.Crop` | 四点透视裁剪、三次插值、复制输入ROI边缘、高宽比达到阈值的90度旋转。 |
| `predict_system.py` | `OcrReader.SortReferenceBoxes` | 左上顶点按 y/x 稳定排序、相邻行差小于10像素时按横向顺序交换；不再用轴对齐框重叠比较排序。 |
| `predict_rec.py`, `rec_postprocess.py` | `OcrReader`, `OcrRecognition.Decode` | 按矫正后文本图的宽高比进行六图批处理，动态宽度、右侧零填充、CTC去重/blank/space及平均分；识别结果回写原框顺序，以0.5置信度过滤，无额外 Trim/空白过滤。 |

调用层继续使用 `OcrLine.Box` 作为源图四点框的轴对齐包围范围，按当前显示尺寸投影，不能用识别裁剪图坐标代替原图坐标。额外保留 `OcrLine.NativeQuad`（原始分辨率和源 ROI 偏移后的四点）与 `RawText`（未转换的 CTC 文本），便于核对原始输出，不改变任务已有的 `Box` / `Text` 输入保护。OKWW 的角度分类器未启用，WW 也不新增方向分类模型。繁体识别仍保留原始文本；Windows 使用 NLS，macOS 离线验证使用系统 CoreFoundation 的 ICU `Traditional-Simplified` 转换，涵盖增补平面的汉字并保留非汉字内容。锁定的 ok-script 3.0.6 繁简转换具体实现尚不可得；不把系统转换与其内部算法宣称等价。

### 推理提供者

锁定 OnnxOCR `predict_base.py` 的非 OpenVINO 分支指定 `CPUExecutionProvider`，开启 `ORT_ENABLE_ALL`。WW 明确采用同一 CPU provider 和全部图优化，保持顺序执行、内存 arena / pattern 与原有线程限制（2 个 intra-op、1 个 inter-op）；每个 reader 完整释放 session options，识别 session 初始化失败时也释放已建立的检测 session。WW 不向磁盘写入优化模型缓存；线程配额和模型缓存策略仍与参考 CPU fallback 不同，不影响模型/字典本身相同的证据。

参考项目 `config.py` 明确启用 OpenVINO 并优先探测 NPU，`predict_base.py` 在支持时使用对应静态适配与设备编译，无法使用 NPU 时才回退 OpenVINO CPU。当前 Windows 发布包及 macOS 离线环境只有支持的 ONNX Runtime CPU 路径，未集成或验证 OpenVINO/NPU 运行时，未增加 Python 依赖，也不声称支持这些硬件路径。

## 验证边界

本轮在 macOS 使用 Unity Mono/Roslyn 与 .NET Framework 4.8 兼容参考程序集：生产源码、测试源码、特征生成器均编译通过，警告视为错误。新增纳入选测的 OCR 13 项（既有输入/评分/窄框、参考排序和批宽、DB 5 项、透视/竖文/CTC 4 项）全部通过；联合245项选测中244项通过。惟 Windows ONNX 原生库相关 `AccountChecks.CancelledPlans` 在本机不可运行。未进行真实游戏操作。

同款权重和字典已经由完整文件指纹证明一致。独立 C# 几何实现（轮廓栅格化、圆角扩边及插值）与 native OpenCV/PyClipper 的边缘舍入仍须相同截图对照。推理提供者也不同（WW ONNX Runtime；参考默认 OpenVINO、可用时 NPU），因此不能仅凭源码流程或合成测试宣称同一截图 OCR 输出像素级完全一致。

第六轮已重新通过 C# 5 / .NET Framework 4.8 参考程序集生产与测试程序编译（警告视为错误）。OCR 合成测试 17/17：除既有 detector/recognizer、候选数及四点透视测试，新增裁剪斜框的 33 像素 mask（检测此前会漏计/误计的边缘）、300 组旋转矩形对比独立穷举面积、300 组轮廓对比独立连通域/孔洞计数、macOS 实际繁简转换及 Unicode 保留。七组使用用户归档的原始参考截图、相同模型的本地 C# 实际 ONNX CPU 推理检查全部通过：原分辨率 ROI / RawText / NativeQuad 投影、声骸强化、体力本领奖、梦魇入口/吸收、地面拾取/下一条拾取。没有调用真实客户端；这些是 C# 对参考归档断言的回放，不是同帧运行原版 OpenVINO/NPU。

剩余不可证差异：锁定 OpenCV 5.0.0.93 / PyClipper 1.4.0 的完整原生像素边界/旋转矩形及极端舍入规则、ok-script 3.0.6 的 OCR 后处理门控与繁简转换上下文、实际 OpenVINO/NPU 提供者。未取得这些依赖的精确实现或设备，不能宣称全部输出等价。

Windows 完整验收：运行项目正常构建脚本的离线测试，使用 `-ReferenceDirectory` 指向本地 OKWW 截图目录验证原分辨率识别、投影和截图回放。不需要运行真实游戏或进行实际账户操作进行这些样本验证。
