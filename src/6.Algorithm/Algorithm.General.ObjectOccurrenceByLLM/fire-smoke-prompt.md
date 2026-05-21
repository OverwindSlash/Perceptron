当前画面中是否有 “火” 或者 “烟”

如有请按照以下 Json 给出类别和位置：
{
  "isObjOccurred": true,
  "occurredObjects": [
    {
      "type": "fire",
      "conf": 0.6,
      "bbox_2d": [1, 2, 3, 4]
    },
    {
      "type": "smoke",
      "conf": 0.4,
      "bbox_2d": [5, 6, 7, 8]
    }
  ]
}

如没有，请输出 Json：
{
  "isObjOccurred": false,
  "occurredObjects": []
}