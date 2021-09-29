using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
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

        public static ConfigEntry<bool> enableOnTheSpot;

        void Awake()
        {
            Logger = base.Logger;
            //Logger.LogInfo("Awake");

            enableOnTheSpot = Config.Bind("General", "enableOnTheSpot", true,
                "Enable OnTheSpot mode when Ctrl is down");
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

            bool hasOutput = false;
            bool hasInput = false;
            for (int i = 0; i < 4; i++)
            {
                bool isOutput;
                int otherObjId;
                int otherSlot;
                tool.factory.ReadObjectConn(objId, i, out isOutput, out otherObjId, out otherSlot);
                if (otherObjId != 0)
                {
                    if (isOutput)
                    {
                         hasInput  = true;
                    }
                    else
                    {
                        hasOutput = true;
                    }
                }
            }

            if (hasInput == hasOutput)
            {
                return EBeltIO.None;
            }
            else if (hasInput)
            {
                return EBeltIO.Input;
            }
            else if (hasOutput)
            {
                return EBeltIO.Output;
            }
            return EBeltIO.None;
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
            if (tool.buildPreviews[0].coverObjId != 0)
            {
                startBeltIO = GetBeltIO(tool, tool.buildPreviews[0].coverObjId);
                if (startBeltIO == EBeltIO.None)
                {
                    return false;
                }
            }

            if (tool.buildPreviews[bpCount - 1].coverObjId != 0)
            {
                endBeltIO = GetBeltIO(tool, tool.buildPreviews[bpCount - 1].coverObjId);
                if (endBeltIO == EBeltIO.None)
                {
                    return false;
                }
            }

            if (tool.buildPreviews[bpCount - 1].outputObjId != 0)
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


        static public bool IsBeltEdge(BuildTool_Path tool, int objId, Vector3 direction, out bool isStraight)
        {
            isStraight = false;

            EBeltIO beltIO = GetBeltIO(tool, objId);
            if (beltIO != EBeltIO.Input && beltIO != EBeltIO.Output)
            {
                return false;
            }

            bool result = false;

            if (objId > 0)
            {
                EntityData e = tool.factory.entityPool[objId];
                BeltComponent belt = tool.factory.cargoTraffic.beltPool[e.beltId];

                if (belt.backInputId != 0 || belt.rightInputId != 0 || belt.leftInputId != 0)
                {
                    if (belt.outputId == 0)
                    {
                        result = true;
                    }
                }
                else if (belt.outputId != 0)
                {
                    result = true;
                }
                if (result)
                {
                    CargoPath cargoPath = tool.factory.cargoTraffic.GetCargoPath(belt.segPathId);
                    Quaternion beltRot = cargoPath.pointRot[belt.segIndex];
                    float dot = Vector3.Dot(beltRot * Vector3.forward, direction);
                    isStraight = Math.Abs(dot) > 0.9f;
                }
            }
            else
            {
                PrebuildData prebuildData = tool.factory.prebuildPool[-objId];
                if (prebuildData.id == -objId)
                {
                    result = true;
                    Quaternion rot = prebuildData.rot;
                    float dot = Vector3.Dot(rot * Vector3.forward, direction);
                    isStraight = Math.Abs(dot) > 0.9f;
                }
            }

            return result;
        }
        static public int GetNearBeltEdge(BuildTool_Path tool)
        {
            float gridSize = 0.6f; //グリッドサイズ(1.3くらい)に近いと取りこぼすので細かく刻む
            Vector3 cursorPos = tool.cursorTarget;
            Quaternion rot = Maths.SphericalRotation(cursorPos, 0f);

            Vector3[] directions = new Vector3[] { rot.Right(), rot.Left(), rot.Forward(), rot.Back() };
            int[] eids = new int[] { 0, 0, 0, 0 };
            int[] distances = new int[] { 999, 999, 999, 999 };
            bool[] isStraights = new bool[] { false, false, false, false };

            for (int idx = 0; idx < directions.Length; idx++)
            {
                for (int k = 0; k <= 40; k++)
                {
                    bool foundAnything = false;
                    Vector3 pos = cursorPos + (directions[idx] * (gridSize * k));

                    BuildToolAccess.nearObjectCount = tool.actionBuild.nearcdLogic.GetBuildingsInAreaNonAlloc(pos, 0.4f, BuildToolAccess.nearObjectIds, false);
                    for (int i = 0; i < BuildToolAccess.nearObjectCount; i++)
                    {
                        int eid = BuildToolAccess.nearObjectIds[i];
                        if (eid != 0)
                        {
                            if (k == 0)
                            {
                                //真下になんかある
                                return 0;
                            }

                            if (IsBeltEdge(tool, eid, directions[idx], out bool isStraight2))
                            {
                                eids[idx] = eid;
                                distances[idx] = k;
                                isStraights[idx] = isStraight2;
                            }

                            foundAnything = true;
                            break;
                        }
                    }
                    if (foundAnything)
                    {
                        break;
                    }
                }
            }

            int min = distances[0];
            int nearestEid = eids[0];
            bool isStraight = isStraights[0];
            for (int idx = 1; idx < directions.Length; idx++)
            {
                if (isStraights[idx] && eids[idx] != 0 && min > distances[idx])
                {
                    nearestEid = eids[idx];
                    min = distances[idx];
                    isStraight = true;
                }
            }
            if (!isStraight)
            {
                min = distances[0];
                nearestEid = eids[0];
                for (int idx = 1; idx < directions.Length; idx++)
                {
                    if (eids[idx] != 0 && min > distances[idx])
                    {
                        nearestEid = eids[idx];
                        min = distances[idx];
                    }
                }
            }

            return nearestEid;
        }

        static public void OnTheSpot(BuildTool_Path tool)
        {
            if (tool.cursorTarget == _lastSpotPos && tool.startObjectId == _lastSpotEid)
            {
                return;
            }
            int eid = GetNearBeltEdge(tool);
            if (eid != 0)
            {
                tool.castObjectId = eid;
                tool.startObjectId = tool.castObjectId;
                tool.startTarget = tool.GetObjectPose(tool.startObjectId).position;
                tool.pathPointCount = 0;
                tool.controller.cmd.stage = 1;
                _lastSpotEid = eid;
                _lastSpotPos = tool.cursorTarget;
            }
            else
            {
                _lastSpotEid = 0;
                _lastSpotPos = Vector3.zero;
                tool.controller.cmd.stage = 0;
                tool.actionBuild.model.connGraph.SetPointCount(0, true);
            }
        }
        internal static int _lastSpotEid = 0;
        internal static Vector3 _lastSpotPos = Vector3.zero;

        static class Patch
        {
            internal static bool _doReverse = false;


            [HarmonyPostfix, HarmonyPatch(typeof(BuildTool_Path), "ConfirmOperation")]
            public static void BuildTool_Path_ConfirmOperation_Postfix(BuildTool_Path __instance, ref bool __result)
            {
                int stage = __instance.controller.cmd.stage;
                if (enableOnTheSpot.Value)
                {
                    if (VFInput.control)
                    {
                        OnTheSpot(__instance);
                    }
                }

                _doReverse = false;
                if (IsReverseSituation(__instance))
                {
                    __instance.actionBuild.model.cursorText += " (Reverse)";
                    ConnGizmoGraph graph = __instance.actionBuild.model.connGraph;
                    for (int i = 1; i < graph.pointCount - 1; i++)
                    {
                        graph.colors[i] = i % 2 == 0 ? 1U : 3U;
                    }
                    _doReverse = true;
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


        public class BuildToolAccess : BuildTool
        {

            public static int[] nearObjectIds
            {
                get
                {
                    return _nearObjectIds;
                }

            }
            public static int nearObjectCount
            {
                get
                {
                    return _nearObjectCount;
                }
                set
                {
                    _nearObjectCount = value;
                }
            }
            public static int[] overlappedIds
            {
                get
                {
                    return _overlappedIds;
                }

            }
            public static int overlappedCount
            {
                get
                {
                    return _overlappedCount;
                }
            }
        }


    }
}
