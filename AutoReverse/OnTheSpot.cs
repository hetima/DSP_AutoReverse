using System;
using UnityEngine;

namespace HTAutoReverse
{
    public class OnTheSpot
    {
        static public bool IsBeltEdge(BuildTool tool, int objId, Vector3 direction, out bool isStraight)
        {
            isStraight = false;

            EBeltIO beltIO = AutoReverse.GetBeltIO(tool, objId, out int otherObjId);
            if (beltIO != EBeltIO.Input && beltIO != EBeltIO.Output)
            {
                return false;
            }

            bool result = false;

            if (objId > 0)
            {
                EntityData e = tool.factory.entityPool[objId];
                if (e.id == objId)
                {
                    //result = true;
                    //Quaternion rot = e.rot;
                    //float dot = Vector3.Dot((rot * Vector3.forward).normalized, direction);
                    //isStraight = Math.Abs(dot) > 0.9f;
                    //タイミングによってはprebuildと繋がった丸い状態になってる時がある
                    //なので繋がってる方向で判別する
                    result = true;
                    Vector3 otherPos;
                    if (otherObjId > 0)
                    {
                        otherPos = tool.factory.entityPool[otherObjId].pos;
                    }
                    else
                    {
                        otherPos = tool.factory.prebuildPool[-otherObjId].pos;
                    }
                    float dot = Vector3.Dot((e.pos - otherPos).normalized, direction);
                    isStraight = Mathf.Abs(dot) > 0.9f;
                }
            }
            else
            {
                PrebuildData prebuildData = tool.factory.prebuildPool[-objId];
                if (prebuildData.id == -objId)
                {
                    //ベルトのprebuildの向きは座標に沿うので判別不可能 南北 == forward/back
                    //繋がってる方向を調べる
                    result = true;
                    Vector3 otherPos;
                    if (otherObjId > 0)
                    {
                        otherPos = tool.factory.entityPool[otherObjId].pos;
                    }
                    else
                    {
                        otherPos = tool.factory.prebuildPool[-otherObjId].pos;
                    }
                    float dot = Vector3.Dot((prebuildData.pos - otherPos).normalized, direction);
                    isStraight = Mathf.Abs(dot) > 0.9f;


                }
            }

            return result;
        }

        //東西南北4方向に存在するベルトの端を探す
        //まっすぐ繋がるものを優先 見つからなければ角度関係なく繋がるもの
        static public int GetNearBeltEdge(BuildTool_Path tool)
        {
            ParallelBuild.Reset();

            float gridSize = 1.2f; //Snap させるので正確でなくてよい
            Vector3 cursorPos = tool.cursorTarget;
            var directionFunc = new Func<Quaternion, Vector3>[] { Maths.Right, Maths.Left, Maths.Forward, Maths.Back };
            var reverseFuncs = new Func<Quaternion, Vector3>[] { Maths.Left, Maths.Right, Maths.Back, Maths.Forward };
            int[] eids = new int[] { 0, 0, 0, 0 };
            int[] distances = new int[] { 999, 999, 999, 999 };
            bool[] isStraights = new bool[] { false, false, false, false };

            int range = AutoReverse.onTheSpotRange.Value;
            if (range < 1)
            {
                range = 1;
            }
            if (range > 100)
            {
                range = 100;
            }

            for (int idx = 0; idx < directionFunc.Length; idx++)
            {
                Vector3 pos = cursorPos;
                Vector3 direction = directionFunc[idx](Maths.SphericalRotation(pos, 0f)).normalized;
                for (int k = 0; k <= range; k++)
                {
                    bool foundAnything = false;
                    pos = tool.actionBuild.planetAux.Snap(pos, tool.castTerrain);

                    //GetColliderData で情報取れるのはカーソル位置から11グリッド程度まで
                    //ActiveEntityBuildCollidersInArea を呼んで都度更新
                    //このメソッドは使われていないのでずっと使えるか不安
                    tool.actionBuild.nearcdLogic.ActiveEntityBuildCollidersInArea(pos, 0.3f);
                    //BuildingTriggers = 425984 //Prebuild + BuildingCollider + BuildPreview
                    int colcnt = Physics.OverlapSphereNonAlloc(pos, 0.3f, BuildToolAccess.TmpCols, 425984, QueryTriggerInteraction.Collide);

                    //2.0.1までの方法 隣接する建物もヒットして除外するのが面倒
                    //BuildToolAccess.nearObjectCount = tool.actionBuild.nearcdLogic.GetBuildingsInAreaNonAlloc(pos, 0.1f, BuildToolAccess.nearObjectIds, false);
                    for (int i = 0; i < colcnt; i++)
                    {
                        int eid = 0;
                        if (tool.planet.physics.GetColliderData(BuildToolAccess.TmpCols[i], out ColliderData colliderData))
                        {
                            if (colliderData.objType == EObjectType.Entity)
                            {
                                eid = colliderData.objId;
                            }
                            else if (colliderData.objType == EObjectType.Prebuild)
                            {
                                eid = -colliderData.objId;
                            }
                        }
                        if (eid != 0)
                        {
                            if (tool.ObjectIsInserter(eid))
                            {
                                //ソーターは無視
                                continue;
                            }
                            if (k == 0)
                            {
                                //真下になんかある
                                return 0;
                            }

                            if (IsBeltEdge(tool, eid, direction, out bool isStraight2))
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
                    //少しくらいのずれは Snap させるので問題ないが、極付近は大きくずれるので修正
                    if (k % 3 == 2)
                    {
                        direction = directionFunc[idx](Maths.SphericalRotation(pos, 0f)).normalized;
                    }

                    pos += (direction * gridSize);

                }
            }

            int min = distances[0];
            int nearestEid = eids[0];
            bool isStraight = isStraights[0];
            Func<Quaternion, Vector3> reverseFunc = reverseFuncs[0];
            //まっすぐ繋がるもの
            for (int idx = 1; idx < directionFunc.Length; idx++)
            {
                if (isStraights[idx] && eids[idx] != 0 && (min > distances[idx] || !isStraight))
                {
                    nearestEid = eids[idx];
                    min = distances[idx];
                    reverseFunc = reverseFuncs[idx];
                    isStraight = true;
                }
            }
            if (isStraight && AutoReverse.ParallelBuildEnabled)
            {
                ParallelBuild.UpdateState(tool, nearestEid, reverseFunc);
            }
            //繋がるもの
            if (!isStraight)
            {
                if (AutoReverse.enableBentConnection.Value && !AutoReverse.ParallelBuildEnabled)
                {
                    min = distances[0];
                    nearestEid = eids[0];
                    for (int idx = 1; idx < directionFunc.Length; idx++)
                    {
                        if (eids[idx] != 0 && min > distances[idx])
                        {
                            nearestEid = eids[idx];
                            min = distances[idx];
                        }
                    }
                }
                else
                {
                    nearestEid = 0;
                }
            }

            return nearestEid;
        }

       

        static public void UpdateState(BuildTool_Path tool)
        {
            if (tool.cursorTarget == _lastSpotPos && tool.startObjectId == _lastSpotEid)
            {
                return;
            }
            int eid = GetNearBeltEdge(tool);
            if (eid != 0)
            {
                //見つかったベルトをクリックして敷き始めたことにする 方向は AutoReverse に任せるので気にしない
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
                _lastSpotPos = tool.cursorTarget;
                tool.controller.cmd.stage = 0;
                tool.actionBuild.model.connGraph.SetPointCount(0, true);
            }


        }
        internal static int _lastSpotEid = 0;
        internal static Vector3 _lastSpotPos = Vector3.zero;
    }


}
