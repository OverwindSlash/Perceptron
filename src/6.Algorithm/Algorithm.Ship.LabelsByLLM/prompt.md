# 船舶视觉属性标注提示词

只根据图片中**清晰可见的视觉证据**分析主要船舶，不要根据常识、背景、海域、场景、旗帜或用途猜测。

1.船舶类型(单个属性)：Cargo, Tanker, Passenger, Workboat, LawEnforcement, Fishing, Leisure, Sailing, Other
2.船体颜色(多个属性)：White, Blue, Gray, Black, Red, Orange, Yellow, Green, Brown, Silver, Other
3.吃水状态(单个属性)：Shallow, Medium, Deep, Unknown

## 输出要求

只输出严格 JSON，不要解释，不要 Markdown。
{
    "ShipType": "Bulker",
    "ShipColor": ["Red", "Black"],
    "ShipDraught": "：Shallow"
}

规则：
- 根据视觉特征判断船舶类型。
- 只标注主要船舶船体、上层建筑、甲板外观中的主要颜色。
- 可多选，但只保留占比明显或有结构意义的颜色。
- 不计入集装箱、海水、天空、码头、建筑、车辆、阴影、反光、污渍、锈迹、小面积文字颜色。
- 吃水状态根据船舶露出水面的高度进行判断