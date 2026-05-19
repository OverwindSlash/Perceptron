分析图片中船舶的以下属性：
1.船舶类型(单个属性)：Cargo, Tanker, Passenger, Workboat, LawEnforcement, Military, Fishing, Leisure, Sailing, Other, Unknown
2.船体颜色(多个属性)：White, Blue, Gray, Black, Red, Orange, Yellow, Green, Brown, Silver, Other
3.吃水状态(单个属性)：Shallow, Medium, Deep, Unknown
属性结果以 Json 形式返回结果。样例 Json 格式如下
{
    "ShipType": "Bulker",
    "ShipColor": ["Red", "Black"],
    "ShipDraught": "：Shallow"
}