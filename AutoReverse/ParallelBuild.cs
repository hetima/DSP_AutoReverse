using System;
using System.Collections.Generic;
using UnityEngine;


namespace HTAutoReverse
{
    public class ParallelBuild
    {
        public struct PathInfo
        {
            public List<Vector3> path;
            public bool isOutgoing;
            public int beltObjId;
            public int beltSlot;
        }


        public static List<PathInfo> _pathInfos = null;

        //作成
        static public void CreatePrebuilds(BuildTool_Path tool)
        {
            if (_pathInfos == null)
            {
                return;
            }

            foreach (var pathInfo in _pathInfos)
            {
                List<BuildPreview> buildPreviews = CreatePrebuild(tool, pathInfo);
                if (buildPreviews != null)
                {
                    tool.buildPreviews.AddRange(buildPreviews);
                }

            }

            _pathInfos = null;
        }

        //1本作成
        static public List<BuildPreview> CreatePrebuild(BuildTool_Path tool, PathInfo pathInfo)
        {
            //ベルトの種類
            ItemProto proto = tool.GetItemProto(pathInfo.beltObjId);
            PrefabDesc prefabDesc = tool.GetPrefabDesc(pathInfo.beltObjId);
            if (proto == null || prefabDesc == null)
            {
                return null;
            }

            List<BuildPreview> buildPreviews = new List<BuildPreview>(pathInfo.path.Count);

            //path[0] はベルト位置なので飛ばす
            for (int i = 1; i < pathInfo.path.Count; i++)
            {
                Vector3 pos = pathInfo.path[i];
                BuildPreview buildPreview = new BuildPreview();
                buildPreview.ResetAll();
                buildPreview.item = proto;
                buildPreview.desc = prefabDesc;
                buildPreview.needModel = false;
                buildPreview.isConnNode = true;
                //buildPreview.genNearColliderArea2 = 1f; //作る直前なのでこれをセットする必要はないと思う
                buildPreview.lpos = pos;
                buildPreview.lpos2 = pos;

                //buildPreview.inputObjId = this.castObjectId;
                buildPreviews.Add(buildPreview);
            }
            for (int i = 0; i < buildPreviews.Count; i++)
            {
                if (i==0)
                {
                    //想定外のベルトではここまで実行されないはずなので決め打ち
                    if (pathInfo.isOutgoing)
                    {
                        buildPreviews[0].input = null;
                        buildPreviews[0].inputObjId = pathInfo.beltObjId;
                        buildPreviews[0].inputFromSlot = 0; //
                        buildPreviews[0].inputToSlot = 1;
                        buildPreviews[0].inputOffset = 0;
                    }
                    else
                    {
                        buildPreviews[i].output = null;
                        buildPreviews[i].outputObjId = pathInfo.beltObjId;
                        buildPreviews[i].outputFromSlot = 0;
                        buildPreviews[i].outputToSlot = 1; //
                        buildPreviews[i].outputOffset = 0;
                    }
                }

                if (i != buildPreviews.Count - 1)
                {
                    if (pathInfo.isOutgoing)
                    {
                        buildPreviews[i].output = buildPreviews[i + 1];
                        buildPreviews[i].outputObjId = 0;
                        buildPreviews[i].outputFromSlot = 0;
                        buildPreviews[i].outputToSlot = 1;
                        buildPreviews[i].outputOffset = 0;
                    }
                }
                if (i != 0)
                {
                    if (!pathInfo.isOutgoing)
                    {
                        buildPreviews[i].output = buildPreviews[i - 1];
                        buildPreviews[i].outputObjId = 0;
                        buildPreviews[i].outputFromSlot = 0;
                        buildPreviews[i].outputToSlot = 1;
                        buildPreviews[i].outputOffset = 0;
                    }
                }
            }
            return buildPreviews;
        }

        //プレビュー表示
        static public void UpdatePreview(BuildTool_Path tool)
        {
            if (_pathInfos == null)
            {
                return;
            }
            uint color = 4U;

            foreach (PathInfo pathInfo in _pathInfos)
            {
                for (int i = 0; i < pathInfo.path.Count; i++)
                {
                    Vector3 pos = pathInfo.path[i];
                    Vector3 nextPos;
                    Quaternion rot = Maths.SphericalRotation(pos, 0f);

                    if (i == 0 || i + 1 == pathInfo.path.Count)
                    {
                        tool.actionBuild.model.connRenderer.AddBlueprintBeltMajorPoint(pos, rot, color);
                        if (i == 0 && pathInfo.isOutgoing)
                        {
                            nextPos = pathInfo.path[i + 1];
                            tool.actionBuild.model.connRenderer.AddBlueprintBeltConn(pos, nextPos, color);
                        }else if (i + 1 == pathInfo.path.Count && !pathInfo.isOutgoing)
                        {
                            nextPos = pathInfo.path[i - 1];
                            tool.actionBuild.model.connRenderer.AddBlueprintBeltConn(pos, nextPos, color);
                        }
                    }
                    else
                    {
                        //tool.actionBuild.model.connRenderer.AddBlueprintBeltPoint(pos, rot, color);
                        if (pathInfo.isOutgoing)
                        {
                            nextPos = pathInfo.path[i + 1];
                        }
                        else
                        {
                            nextPos = pathInfo.path[i - 1];
                        }
                        tool.actionBuild.model.connRenderer.AddBlueprintBeltConn(pos, nextPos, color);
                    }
                }
            }
        }

        static public void Reset()
        {
            _pathInfos = null;
        }

        //データ更新
        static public void UpdateState(BuildTool_Path tool, int startObjId, Func<Quaternion, Vector3> reverseFunc)
        {
            _pathInfos = new List<PathInfo>(8);

            int connectedObjId = ConnectedObjId(tool, startObjId);
            if (connectedObjId == 0)
            {
                return;
            }
            for (int j = 0; j <= 11; j++)
            {
                tool.factory.ReadObjectConn(connectedObjId, j, out bool isOutput, out int otherObjId, out int otherSlot);
                if (otherObjId != 0 && tool.ObjectIsInserter(otherObjId))
                {
                    tool.factory.ReadObjectConn(otherObjId, otherSlot == 1 ? 0 : 1, out isOutput, out int anotherObjId, out int anotherSlot);
                    if (anotherObjId != 0 && tool.ObjectIsBelt(anotherObjId))
                    {

                        PathInfo info = ParallelPath(tool, startObjId, reverseFunc, anotherObjId);
                        if (info.path != null && info.path.Count > 1) //1の場合はベルトの位置のみなので追加しなくて良い
                        {
                            _pathInfos.Add(info);
                        }
                    }
                }
            }
        }

        //ベルトから端を探してリスト作成まで
        //反対側の端に繋がってる可能性もあるので方向を調べる
        static public PathInfo ParallelPath(BuildTool_Path tool, int startObjId, Func<Quaternion, Vector3> reverseFunc, int objId)
        {
            int edgeBeltId = 0;
            PathInfo info = new PathInfo();

            
            //ベルトの端まで辿る
            int limit = 300; //
            while (objId != 0 && limit > 0)
            {
                limit--;
                int nextId = 0;

                Vector3 pos = tool.GetObjectPose(objId).position;
                Vector3 direction = reverseFunc(Maths.SphericalRotation(pos, 0f)).normalized;

                int connCount = 0;
                bool isOutgoing = false;
                for (int i = 0; i < 4; i++)
                {
                    tool.factory.ReadObjectConn(objId, i, out bool isOutput, out int otherId, out int otherSlot);
                    if (otherId != 0)
                    {
                        Vector3 pos2 = tool.GetObjectPose(otherId).position;

                        float dot = Vector3.Dot((pos2 - pos).normalized, direction);
                        if (dot > 0.9f)
                        {
                            nextId = otherId;
                        }
                        //else if(prevId != 0 && otherId != prevId) //曲がってても一応続ける //レアケースなので無視
                        //{
                        //    nextId = otherId;
                        //}
                        connCount++;
                        isOutgoing = !isOutput;
                        //info.beltSlot = i >= 2 ? i - 2 : i + 2;  // i == 0 ? 1 : 0;

                    }
                }
                //prevId = objId;
                //connCount == 1ならベルトの端 nextId == 0 なら近い側の端
                if (connCount == 1 && nextId == 0)
                {
                    //端
                    edgeBeltId = objId;
                    info.isOutgoing = isOutgoing;
                    break;
                }
                else
                {
                    //reverse方向のベルトへ
                    objId = nextId;
                }

            }

            //マウスカーソルのある座標まで戻る
            if (edgeBeltId != 0 && startObjId != edgeBeltId)
            {
                //戻る
                List<Vector3> path = new List<Vector3>(AutoReverse.onTheSpotRange.Value);
                Vector3 pos = tool.GetObjectPose(edgeBeltId).position;
                path.Add(pos); //プレビュー用にベルトの位置も追加

                Vector3 cursorPos = tool.cursorTarget;
                limit = 300;
                float gridSize = 1.2f;
                float prevDist = (cursorPos - pos).sqrMagnitude;
                while (limit > 0)
                {
                    limit--;
                    Vector3 direction = reverseFunc(Maths.SphericalRotation(pos, 0f)).normalized;
                    pos += (direction * gridSize);
                    pos = tool.actionBuild.planetAux.Snap(pos, tool.castTerrain);

                    //障害物チェック
                    tool.actionBuild.nearcdLogic.ActiveEntityBuildCollidersInArea(pos, 0.3f);
                    //BuildingTriggers = 425984 //Prebuild + BuildingCollider + BuildPreview
                    int colcnt = Physics.OverlapSphereNonAlloc(pos, 0.3f, BuildToolAccess.TmpCols, 425984, QueryTriggerInteraction.Collide);
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
                        //何かにぶつかった
                        if (eid != 0)
                        {
                            return info;
                            //ソーターにちゃんと繋がるか不明なのでとりあえず無視しない
                            //if (tool.ObjectIsInserter(eid))
                            //{
                            //    continue; //ソーターは無視
                            //}
                            //else
                            //{
                            //    return null;
                            //}
                        }
                    }
                    float dist = (cursorPos - pos).sqrMagnitude;
                    if (dist > prevDist)
                    {
                        break;
                    }
                    prevDist = dist;
                    path.Add(pos);
                }

                info.path = path;
                info.beltObjId = edgeBeltId;
                

                return info;
            }


            return info;

        }


        //ベルトと繋がっている建物を探す
        static public int ConnectedObjId(BuildTool tool, int beltId)
        {
            int result = 0;
            if (beltId == 0)
            {
                return 0;
            }
            int prevBeltId = 0;
            int limit = 300; //
            while (beltId != 0 && limit > 0)
            {
                limit--;
                int recipe = 0;
                GetBeltConn(tool, beltId, prevBeltId, out int otherBeltId, out int connectedObjId);
                if (connectedObjId != 0)
                {
                    if (connectedObjId > 0)
                    {
                        EntityData e = tool.factory.entityPool[connectedObjId];
                        if (e.id == connectedObjId && e.assemblerId != 0)
                        {
                            recipe = 1; //tool.factory.factorySystem.assemblerPool[e.assemblerId].recipeId;
                        }
                        else if (e.id == connectedObjId && e.labId != 0)
                        {
                            recipe = 1;
                        }
                    }
                    else
                    {
                        PrebuildData prebuildData = tool.factory.prebuildPool[-connectedObjId];
                        if (prebuildData.id == -connectedObjId)
                        {
                            recipe = prebuildData.recipeId;
                        }
                    }

                    if (recipe > 0)
                    {
                        result = connectedObjId;
                        return result;
                    }
                }
                prevBeltId = beltId;
                beltId = otherBeltId;
            }

            return result;
        }

        //ベルトと繋がっているソーターを探す
        static public void GetBeltConn(BuildTool tool, int beltId, int prevBeltId, out int otherBeltId, out int connectedObjId)
        {
            otherBeltId = 0;
            connectedObjId = 0;

            for (int i = 0; i < 16; i++)
            {
                tool.factory.ReadObjectConn(beltId, i, out bool isOutput, out int otherObjId, out int otherSlot);
                if (otherObjId == 0 || otherObjId == prevBeltId) continue;

                if (tool.ObjectIsBelt(otherObjId))
                {
                    otherBeltId = otherObjId;
                }
                else if (tool.ObjectIsInserter(otherObjId))
                {
                    tool.factory.ReadObjectConn(otherObjId, otherSlot == 0 ? 1 : 0, out bool isOutput2, out int otherObjId2, out int otherSlot2);
                    connectedObjId = otherObjId2;
                }
            }
        }
    }


}
