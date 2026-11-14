namespace SpatialTracking {
 public static class CalibrationGuide {
  public static readonly string[] Names={"水平位置","垂直位置","横向跟随","纵向跟随","距离校正"};
  public static readonly string[] Instructions={
   "将头移到正对场景中心的位置，调整到左右墙面显示对称。",
   "保持头部正对场景中心，调整到顶部与底部显示对称。",
   "将头移到正对场景最左侧墙面的位置，调至左墙刚好消失。\n再移到正对最右侧墙面的位置，检查右墙是否刚好消失。",
   "眼睛高度对齐三维预览区域上沿，调至顶墙刚好消失。\n再对齐三维区域下沿，检查底面；不以显示器外框为参照。",
   "左右、上下和前后移动头部，观察中心立方体。\n调整到空间透视自然，立方体没有压扁或拉长感。"};
  public static bool CanInitialize(CalibrationProfile profile)=>profile.schema_version==2&&profile.revision==0&&!profile.manual.Bound;
  public static bool AcceptUndoShortcut(bool calibration,bool textEditing,bool modal)=>calibration&&!textEditing&&!modal;
 }
}
