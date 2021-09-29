using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HTAutoReverse
{
    [BepInPlugin(__GUID__, __NAME__, "1.0.1")]
    public class AutoReverse : BaseUnityPlugin
    {
        public const string __NAME__ = "AutoReverse";
        public const string __GUID__ = "com.hetima.dsp." + __NAME__;

        new internal static ManualLogSource Logger;

        void Awake()
        {
            Logger = base.Logger;
            //Logger.LogInfo("Awake");

            new Harmony(__GUID__).PatchAll(typeof(Patch));
        }

        public enum EBeltIO
        {
            NotBelt, //ベルトではない
            None, //In Out繋がってる
            Input,
            Output,
        }

        static public bool ObjectIsMiner(BuildTool_Path tool, int objId)
        {
            if (objId == 0)
            {
                return false;
            }
            ItemProto proto;
            if (objId > 0)
            {
                proto = LDB.items.Select(tool.factory.entityPool[objId].protoId);
            }
            else
            {
                proto = LDB.items.Select(tool.factory.prebuildPool[-objId].protoId);
            }

            if (proto == null || proto.prefabDesc == null)
            {
                return false;
            }
            if (proto.prefabDesc.veinMiner || proto.prefabDesc.oilMiner)
            {
                return true;
            }
            return false;
        }

        static public EBeltIO GetBeltIO(BuildTool_Path tool, int objId)
        {
            if (objId == 0 || !tool.ObjectIsBelt(objId))
            {
                return EBeltIO.NotBelt;
            }
            EntityData e = tool.factory.entityPool[objId];
            BeltComponent belt = tool.factory.cargoTraffic.beltPool[e.beltId];

            if (belt.backInputId != 0 || belt.rightInputId != 0 || belt.leftInputId != 0)
            {
                if (belt.outputId != 0)
                {
                    return EBeltIO.None;
                }
                return EBeltIO.Output;

            }
            if (belt.outputId != 0)
            {
                return EBeltIO.Input;
            }

            return EBeltIO.None; //どこにも繋がっていない丸いやつ
        }

        static public bool IsReverseSituation(BuildTool_Path tool)
        {
            int bpCount = tool.buildPreviews.Count;
            if (bpCount < 2)
            {
                return false;
            }
            if (tool.buildPreviews[0].output != tool.buildPreviews[1])
            {
                return false;
            }
            for (int i = 0; i < bpCount; i++)
            {
                BuildPreview b = tool.buildPreviews[i];
                if (b.condition != EBuildCondition.Ok || !b.desc.isBelt || b.outputFromSlot != 0 || b.input != null)
                {
                    return false;
                }
                if (i != 0 && i != bpCount - 1)
                {
                    if (b.coverObjId > 0) return false;
                    if (b.output != tool.buildPreviews[i + 1]) return false;
                }
            }

            EBeltIO startBeltIO = EBeltIO.NotBelt;
            EBeltIO endBeltIO = EBeltIO.NotBelt;
            if (tool.buildPreviews[0].coverObjId > 0)
            {
                startBeltIO = GetBeltIO(tool, tool.buildPreviews[0].coverObjId);
                if (startBeltIO == EBeltIO.None)
                {
                    return false;
                }
            }

            if (tool.buildPreviews[bpCount - 1].coverObjId > 0)
            {
                endBeltIO = GetBeltIO(tool, tool.buildPreviews[bpCount - 1].coverObjId);
                if (endBeltIO == EBeltIO.None)
                {
                    return false;
                }
            }

            if (tool.buildPreviews[bpCount - 1].outputObjId > 0)
            {
                if (ObjectIsMiner(tool, tool.buildPreviews[bpCount - 1].outputObjId))
                {
                    endBeltIO = EBeltIO.Output;
                }
            }

            if (startBeltIO == endBeltIO)
            {
                return false;
            }
            else if(startBeltIO == EBeltIO.Output)
            {
                return false;
            }
            else if (startBeltIO == EBeltIO.Input)
            {
                return true;
            }
            else if(endBeltIO == EBeltIO.Output)
            {
                return true;
            }

            return false;
        }

        static public void ReverseConnection(BuildTool_Path tool)
        {
            int bpCount = tool.buildPreviews.Count;

            //最初を最後に
            BuildPreview b0 = tool.buildPreviews[0];
            b0.output = null;
            if (b0.inputObjId != 0) //splitter や tank など
            {
                b0.outputObjId = b0.inputObjId;
                b0.inputObjId = 0;
                b0.outputToSlot = b0.inputFromSlot;
                b0.inputToSlot = 0;
                b0.inputFromSlot = 0;
            }
            else //(b0.coverObjId != 0)でも同じ処理
            {
                b0.outputToSlot = 0;
                b0.inputFromSlot = 0;
                b0.outputObjId = 0;
            }

            //最後を最初に
            b0 = tool.buildPreviews[bpCount - 1];
            b0.output = tool.buildPreviews[bpCount - 2];
            if (b0.outputObjId != 0) //splitter や tank など
            {
                b0.inputObjId = b0.outputObjId;
                b0.outputObjId = 0;
                b0.inputFromSlot = b0.outputToSlot;
                b0.outputToSlot = 1;
                b0.inputToSlot = 1;
            }
            else //(b0.coverObjId != 0)でも同じ処理
            {
                b0.outputToSlot = 1;
                b0.inputFromSlot = 0;
                b0.inputObjId = 0;
            }

            //中間を反転
            for (int i = 1; i < bpCount - 1; i++)
            {
                BuildPreview b = tool.buildPreviews[i];
                b.output = tool.buildPreviews[i - 1];
            }
        }


        static class Patch
        {
            internal static bool _doReverse = false;

            [HarmonyPostfix, HarmonyPatch(typeof(BuildTool_Path), "ConfirmOperation")]
            public static void BuildTool_Path_ConfirmOperation_Postfix(BuildTool_Path __instance, ref bool __result)
            {
                _doReverse = false;
                if (IsReverseSituation(__instance))
                {
                    __instance.actionBuild.model.cursorText += " (Reverse)";
                    ConnGizmoGraph graph = __instance.actionBuild.model.connGraph;
                    for (int i = 1; i < graph.pointCount - 1; i++)
                    {
                        graph.colors[i] = i % 2 == 0 ? 1U : 3U;
                    }
                    _doReverse = __result;
                }
            }

            [HarmonyPrefix, HarmonyPatch(typeof(BuildTool_Path), "CreatePrebuilds"), HarmonyBefore("dsp.nebula-multiplayer")]
            public static void BuildTool_Path_CreatePrebuilds_Prefix(BuildTool_Path __instance)
            {
                if (_doReverse && IsReverseSituation(__instance))
                {
                    ReverseConnection(__instance);
                    //LogBuildPreviews(__instance);
                }
                _doReverse = false;

            }

            public static void LogBuildPreviews(BuildTool_Path tool)
            {
                Logger.LogInfo("-------------AutoReverse-------------");
                foreach (BuildPreview b in tool.buildPreviews)
                {
                    string info = "";
                    info += " ot:" + b.outputToSlot + " if:" + b.inputFromSlot + " it:" + b.inputToSlot + " of:" + b.outputFromSlot;
                    if (b.output != null) info += " output:" + b.output.objId;
                    if (b.input != null) info += " input:" + b.input.objId;
                    if (b.coverObjId != 0) info += " coverObjId:" + b.coverObjId;
                    if (b.outputObjId != 0) info += " outputObjId:" + b.outputObjId;
                    if (b.inputObjId != 0) info += " inputObjId:" + b.inputObjId;
                    if (b.willRemoveCover) info += " willRemoveCover:true";
                    Logger.LogInfo(info);
                }
            }
        }
    }
}
