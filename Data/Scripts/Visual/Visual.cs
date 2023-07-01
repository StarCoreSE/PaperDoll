using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using CoreSystems.Api;


namespace klime.Visual
{
    //Render grid
    public class GridR
    {
        public MyCubeGrid grid;
        public EntRender entRender;
        public MatrixD controlMatrix;
        public double scale;
        public double tempscale = 0.8;
        Vector3D relTrans;
        Vector3D relForward;
        Vector3D relUp;

        public GridR(MyCubeGrid grid , EntRender entRender = null)
        {
            this.grid = grid;
            this.entRender = entRender;

            if (this.entRender == null)
            {
                entRender = new EntRender();
            }
        }

        public void UpdateMatrix(MatrixD renderMatrix)
        {
            if (grid.MainCockpit != null)
            {
                var finalForward = Vector3D.TransformNormal(relForward , renderMatrix);
                var finalUp = Vector3D.TransformNormal(relUp , renderMatrix);
                var finalWM = MatrixD.CreateWorld(renderMatrix.Translation , finalForward , finalUp);

                finalWM.Translation += Vector3D.TransformNormal(relTrans , finalWM);
                grid.WorldMatrix = finalWM;
            }
            else
            {
                renderMatrix.Translation += Vector3D.TransformNormal(relTrans , renderMatrix);
                grid.WorldMatrix = renderMatrix;
            }
        }


        public void DoRescale()
        {

            var volume = grid.PositionComp.WorldVolume;
            scale = 0.028 / volume.Radius;

            if (grid.GridSizeEnum == MyCubeSize.Small)
            {
                scale *= 0.8;
            }

            if (grid.MainCockpit != null)
            {
                relTrans = Vector3D.TransformNormal(grid.WorldMatrix.Translation - grid.PositionComp.WorldAABB.Center ,
                    MatrixD.Transpose(grid.WorldMatrix));
                relForward = Vector3D.TransformNormal(grid.MainCockpit.WorldMatrix.Forward , MatrixD.Transpose(grid.WorldMatrix));
                relUp = Vector3D.TransformNormal(grid.MainCockpit.WorldMatrix.Up , MatrixD.Transpose(grid.WorldMatrix));

                relTrans *= scale;
                relForward *= scale;
                relUp *= scale;
            }
            else
            {
                relTrans = Vector3D.TransformNormal(grid.WorldMatrix.Translation - grid.PositionComp.WorldAABB.Center ,
                    MatrixD.Transpose(grid.WorldMatrix));

                relTrans *= scale;
            }

            grid.PositionComp.Scale = (float)scale;

            //CreateLights();
        }

        public void DoCleanup()
        {

            HashSet<IMyEntity> subparts = new HashSet<IMyEntity>();
            foreach (var fatblock in grid.GetFatBlocks())
            {
                IMyFunctionalBlock fBlock = fatblock as IMyFunctionalBlock;
                if (fBlock != null)
                {
                    fBlock.Enabled = false;
                }

                IMyExhaustBlock exhaust = fatblock as IMyExhaustBlock;
                if (exhaust != null)
                {
                    exhaust.StopEffects();
                }

            }

            if (grid.IsPowered)
            {
                grid.SwitchPower();
            }

            grid.ChangeGridOwnership(MyAPIGateway.Session.Player.IdentityId , MyOwnershipShareModeEnum.Faction);


            string whiteHex = "#FFFFFF";
            Vector3 whiteHSVOffset = MyColorPickerConstants.HSVToHSVOffset(ColorExtensions.ColorToHSV(ColorExtensions.HexToColor(whiteHex)));
            whiteHSVOffset = new Vector3((float)Math.Round(whiteHSVOffset.X , 2) , (float)Math.Round(whiteHSVOffset.Y , 2) , (float)Math.Round(whiteHSVOffset.Z , 2));

            List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
            IMyCubeGrid iGrid = grid as IMyCubeGrid;
            iGrid.GetBlocks(allBlocks);

            //grid.ColorBlocks(grid.Min, grid.Max, whiteHSVOffset, false, false);
            ////iGrid.ColorBlocks(iGrid.Min, iGrid.Max, whiteHSVOffset);
            ////grid.ColorGrid(whiteHSVOffset, false, false);

            foreach (var block in allBlocks)
            {
                block.Dithering = 0.1f;
                //grid.ChangeColorAndSkin(grid.GetCubeBlock(block.Position), whiteHSVOffset);
            }
            //grid.Render.Transparency = -0.01f;


        }
    }

    //Render elements - damage state etc
    public class EntRender
    {
        public MyLight light;

        public EntRender()
        {
            light = new MyLight();
        }

    }

    //Tracks grid groups
    public class GridG
    {
        public List<GridR> gridGroup;
        public bool doneInitialCleanup = false;
        public bool doneRescale = false;
        public double rotationForward;
        public double rotationUp;
        public double rotationForwardBase;
        public int timer;
        public List<IMyCubeBlock> DelList = new List<IMyCubeBlock>();
        public List<Vector3I> SlimList = new List<Vector3I>();

        public Dictionary<IMyCubeBlock , int> DelDict = new Dictionary<IMyCubeBlock , int>();

        public GridG(List<GridR> gridGroup , double rotationForwardBase)
        {
            this.gridGroup = new List<GridR>(gridGroup); //Allocation?
            this.rotationForwardBase = rotationForwardBase;
        }

        public GridG(GridR gridR , double rotationForwardBase)
        {

            //Adds first grid
            gridGroup = new List<GridR>();
            gridGroup.Add(gridR);
            this.rotationForwardBase = rotationForwardBase;
            //Subgrids later!

        }

        public void DoCleanup()
        {
            foreach (var subgrid in gridGroup)
            {
                if (subgrid.grid != null)
                {
                    subgrid.DoCleanup();
                    doneInitialCleanup = true;
                }
            }
        }

        public void DoRescale()
        {

            foreach (var subgrid in gridGroup)
            {
                if (subgrid.grid != null)
                {
                    subgrid.DoRescale();
                    doneRescale = true;
                }
            }
        }



        public void DoBlockRemove(Vector3I position)
        {
            SlimList.Clear();
            SlimList.Add(position);

            foreach (var subgrid in gridGroup)
            {
                if (subgrid.grid != null)
                {
                    var slim = subgrid.grid.GetCubeBlock(position) as IMySlimBlock;
                    if (slim != null)
                    {
                        if (slim.FatBlock == null)
                        {
                            MyCubeGrid xGrid = subgrid.grid;
                            //MyAPIGateway.Utilities.ShowMessage("Slim Block Removed", "");
                            //xGrid.RazeBlocksClient(SlimList);
                            xGrid.RazeGeneratedBlocks(SlimList);

                        }

                        else
                        {
                            slim.Dithering = 2.5f;
                            
                                //MyVisualScriptLogicProvider.SetHighlight(slim.FatBlock.Name , true, 10 , 5 , Color.Red);
                            
                            //DelList.Add(slim.FatBlock);
                            //MyAPIGateway.Utilities.ShowMessage("Fat Block Removed", "");
                            
                            if (slim.FatBlock.Mass > 1500)
                            {
                                if (!DelDict.ContainsKey(slim.FatBlock))
                                {
                                    MyVisualScriptLogicProvider.SetHighlightLocal(slim.FatBlock.Name , 10 , 10 , Color.Red);
                                    DelDict.Add(slim.FatBlock , timer + 200);
                                }

                            }
                            else
                            {
                                if (!DelDict.ContainsKey(slim.FatBlock))
                                {
                                    DelDict.Add(slim.FatBlock , (timer + 10));
                                }

                            }

                        }

                    }
                }
            }
        }

        public void UpdateMatrix(MatrixD renderMatrix , MatrixD rotMatrix)
        {

            if (!doneRescale || !doneInitialCleanup)
            {
                return;
            }
            timer++;
            DelList.Clear();

            foreach (var fatblock in DelDict.Keys)
            {
                if (DelDict[ fatblock ] == timer)
                {
                    fatblock.Close();
                    DelList.Add(fatblock);
                }
            }

            foreach (var item in DelList)
            {
                DelDict.Remove(item);
            }

            //tried to remove slims

            //foreach (var slimBlock in SlimDelDict.Keys)
            //{
            //    if (SlimDelDict[ slimBlock ] == timer)
            //    {
            //        SlimDelList.Add(slimBlock);
            //    }
            //}

            //foreach (var item in SlimDelList)
            //{
            //    SlimDelDict.Remove(item);
            //}


            //this.rotationForward = rotationForwardBase + rotationForward;
            //this.rotationUp = rotationUp;
            var rotateMatrix = MatrixD.CreateRotationY(rotationForwardBase);
            renderMatrix = rotateMatrix * renderMatrix;

            var origTranslation = renderMatrix.Translation;
            renderMatrix = rotMatrix * renderMatrix;
            renderMatrix.Translation = origTranslation;

            foreach (var subgrid in gridGroup)
            {
                if (subgrid.grid != null)
                {
                    subgrid.UpdateMatrix(renderMatrix);
                }
            }
        }
    }

    //Overall visualization
    public class EntVis
    {
        public MyCubeGrid realGrid;
        public MatrixD realGridBaseMatrix;
        public GridG visGrid;
        public int lifetime;
        public ushort netID = 39302;
        public bool isClosed = false;
        public double xOffset;
        public double yOffset;
        public double rotOffset;
        int timerRot = 0;
        public EntVis(MyCubeGrid realGrid , double xOffset , double yOffset , double rotOffset)
        {
            this.realGrid = realGrid;
            if (realGrid.MainCockpit != null)
            {
                this.realGridBaseMatrix = this.realGrid.MainCockpit.WorldMatrix;
            }
            else
            {
                this.realGridBaseMatrix = this.realGrid.WorldMatrix;
            }
            this.xOffset = xOffset;
            this.yOffset = yOffset;
            this.rotOffset = rotOffset;

            lifetime = 0;

            RegisterEvents();
            GenerateClientGrids();
        }

        private void RegisterEvents()
        {
            UpdateGridPacket regGridPacket = new UpdateGridPacket(realGrid.EntityId , RegUpdateType.Add);
            var byteArray = MyAPIGateway.Utilities.SerializeToBinary(regGridPacket);
            MyAPIGateway.Multiplayer.SendMessageTo(netID , byteArray , MyAPIGateway.Multiplayer.ServerId);

        }

        public void BlockRemoved(Vector3I position)
        {

            if (visGrid != null)
            {
                visGrid.DoBlockRemove(position);
            }
            
        }

        public void GenerateClientGrids()
        {

            var realOB = realGrid.GetObjectBuilder() as MyObjectBuilder_CubeGrid;
            MyEntities.RemapObjectBuilder(realOB); //Remap to avoid duplicate id

            realOB.CreatePhysics = false;
            MyAPIGateway.Entities.CreateFromObjectBuilderParallel(realOB , false , completeCall);

        }

        private void completeCall(IMyEntity obj)
        {
            if (isClosed) return; //if grid is closed don't
            MyCubeGrid visGridCubeGrid = obj as MyCubeGrid;
            visGridCubeGrid.SyncFlag = false;
            visGridCubeGrid.Save = false;
            visGridCubeGrid.RemoveFromGamePruningStructure();
            visGridCubeGrid.Render.CastShadows = false;
            visGridCubeGrid.DisplayName = "";
            MyAPIGateway.Entities.AddEntity(visGridCubeGrid);

            GridR gridR = new GridR(visGridCubeGrid);
            visGrid = new GridG(gridR , rotOffset);
        }

        public void Update()
        {
            UpdateVisLogic();
            UpdateVisPosition();
            UpdateRealLogic();
            lifetime += 1;
        }



        private void UpdateVisPosition()
        {

            IMyCamera playerCamera = MyAPIGateway.Session.Camera;
            if (visGrid != null && realGrid != null && !realGrid.MarkedForClose)
            {
                var renderMatrix = playerCamera.WorldMatrix;
                var moveFactor = 0.6 * playerCamera.FovWithZoom;
                renderMatrix.Translation += renderMatrix.Forward * (0.1 / moveFactor) + renderMatrix.Right * xOffset + renderMatrix.Down * yOffset;

                MatrixD rotMatrix = MatrixD.Identity;
                //Rotation - check bug

                if (realGrid.MainCockpit != null)
                {
                    rotMatrix = realGrid.MainCockpit.WorldMatrix * MatrixD.Normalize(MatrixD.Invert(realGridBaseMatrix));
                }

                else
                {
                    rotMatrix = realGrid.WorldMatrix * MatrixD.Normalize(MatrixD.Invert(realGridBaseMatrix));
                }

                visGrid.UpdateMatrix(renderMatrix , rotMatrix);

            }

        }

        private void UpdateVisLogic()
        {

            if (visGrid != null)
            {
                if (!visGrid.doneInitialCleanup)
                {
                    visGrid.DoCleanup();
                }

                if (!visGrid.doneRescale)
                {
                    visGrid.DoRescale();
                }
            }
        }

        private void UpdateRealLogic()
        {
            if (realGrid == null || realGrid.MarkedForClose || realGrid.Physics == null || !realGrid.IsPowered)
            {
                Close();
            }
        }

        public double AngleBetweenVectorsGrid(Vector3D vectorA , Vector3D vectorB , Vector3D planeNormal)
        {
            vectorA = Vector3D.Normalize(vectorA);
            vectorB = Vector3D.Normalize(vectorB);
            planeNormal = Vector3D.Normalize(planeNormal);

            PlaneD plane = new PlaneD(Vector3D.Zero , planeNormal);
            double angle = MyUtils.GetAngleBetweenVectors(vectorA , vectorB);
            double sign = Vector3D.Dot(plane.Normal , vectorB);

            //if (sign < 0)
            //{
            //    angle = -angle;
            //}

            return angle;
        }

        public void Close()
        {

            if (visGrid != null)
            {
                foreach (var subgrid in visGrid.gridGroup)
                {
                    subgrid.grid.Close();
                }
            }

            UpdateGridPacket updateGridPacket = new UpdateGridPacket(realGrid.EntityId , RegUpdateType.Remove);
            var byteArray = MyAPIGateway.Utilities.SerializeToBinary(updateGridPacket);
            MyAPIGateway.Multiplayer.SendMessageTo(netID , byteArray , MyAPIGateway.Multiplayer.ServerId);

            isClosed = true;

        }
    }

    //Networking

    [ProtoInclude(1000, typeof(UpdateGridPacket))]
    [ProtoInclude(2000, typeof(FeedbackDamagePacket))]
    [ProtoContract]
    public class Packet
    {
        public Packet()
        {

        }
    }

    [ProtoContract]
    public class UpdateGridPacket : Packet
    {
        [ProtoMember(1)]
        public RegUpdateType regUpdateType;

        [ProtoMember(2)]
        public List<long> entityIds;

        public UpdateGridPacket()
        {

        }

        public UpdateGridPacket(List<long> registerEntityIds, RegUpdateType regUpdateType)
        {
            this.entityIds = new List<long>(registerEntityIds);
            this.regUpdateType = regUpdateType;
        }

        public UpdateGridPacket(long registerEntityId, RegUpdateType regUpdateType)
        {
            this.entityIds = new List<long>
            {
                registerEntityId
            };
            this.regUpdateType = regUpdateType;
        }
    }

    [ProtoContract]
    public class FeedbackDamagePacket : Packet
    {
        [ProtoMember(11)]
        public long entityId;

        [ProtoMember(12)]
        public Vector3I position;

        public FeedbackDamagePacket()
        {

        }

        public FeedbackDamagePacket(long entityId, Vector3I position)
        {
            this.entityId = entityId;
            this.position = position;
        }
    }

    public enum RegUpdateType
    {
        Add,
        Remove
    }

    public enum ViewState
    {
        Idle,
        Searching,
        SearchingAll,
        SearchingWC,
        Locked,
        GoToIdle,
        GoToIdleWC,
        DoubleSearching
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Visual : MySessionComponentBase
    {
        //Search variables
        int maxRange = 20000;
        double maxAngleTolerance = 0.174533; //Radians

        //Network
        public ushort feedbackNetID = 38492;
        public ushort netID = 39302;
        Dictionary<ulong, List<IMyCubeGrid>> serverTracker = new Dictionary<ulong, List<IMyCubeGrid>>();

        //Ents
        List<MyEntity> searchEnts = new List<MyEntity>();

        //Core
        int timer = 0;
        bool validInputThisTick = false;
        public ViewState viewState = ViewState.Idle;
        List<EntVis> allVis = new List<EntVis>();

        //API
        WcApi wcAPI;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            
        }

        public override void LoadData()
        {

            //maxRange = MyAPIGateway.Session.SessionSettings.SyncDistance;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(netID , NetworkHandler);
            }

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(feedbackNetID , FeedbackHandler);

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                wcAPI = new WcApi();

                wcAPI.Load(WCRegistered,true);
            }

        }

        private void WCRegistered()
        {
        }

        public override void UpdateAfterSimulation()
        {

            if (MyAPIGateway.Utilities.IsDedicated)
            {
                return;
            }

            IMyCharacter charac = MyAPIGateway.Session.Player?.Character; //No player character - return
            if (charac == null)
            {
                return;
            }

            IMyCamera currentCamera = MyAPIGateway.Session.Camera;
            if (currentCamera == null)
            {
                return;
            }

            if (ValidInput())
            {
                validInputThisTick = true;
            }
            else
            {
                validInputThisTick = false;
            }

            //Check if F2 is pressed
            if (validInputThisTick && IsAdmin(MyAPIGateway.Session.Player))
            {
                if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F2))
                {
                    if (MyAPIGateway.Input.IsAnyShiftKeyPressed())
                    {
                        //if (viewState == ViewState.Idle)
                        //{
                        //    viewState = ViewState.SearchingAll;
                        //}
                        //else
                        //{
                        viewState = ViewState.GoToIdle;
                        //}
                    }
                    else if (MyAPIGateway.Input.IsKeyPress(MyKeys.OemTilde))
                    {
                        if (viewState == ViewState.Idle)
                        {
                            viewState = ViewState.Searching;
                        }
                        else
                        {
                            viewState = ViewState.GoToIdle;
                        }
                    }
                    else
                    {
                        if (viewState == ViewState.Idle)
                        {
                            viewState = ViewState.SearchingWC;
                        }
                        else
                        {
                            viewState = ViewState.GoToIdleWC;
                        }
                    }
                }
            }




            if (viewState == ViewState.Idle)
            {

            }

            if (viewState == ViewState.Searching)
            {
                searchEnts.Clear();

                BoundingSphereD sphere = new BoundingSphereD(currentCamera.WorldMatrix.Translation , maxRange);
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere , searchEnts);

                MyCubeGrid targetGrid = null;
                double currentSmallestAngle = maxAngleTolerance;
                foreach (var ent in searchEnts)
                {
                    MyCubeGrid grid = ent as MyCubeGrid;
                    if (grid?.Physics == null)
                    {
                        continue;
                    }

                    var group = MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Physical , grid);
                    var gridList = new List<IMyCubeGrid>();

                    if (group != null)
                    {
                        group.GetGrids(gridList);
                        if (gridList.Count > 1)
                        {
                            var currentMostBlocks = 0;
                            foreach (var testGrid in gridList)
                            {
                                MyCubeGrid cubeTestGrid = testGrid as MyCubeGrid;
                                if (cubeTestGrid.BlocksCount > currentMostBlocks)
                                {
                                    grid = cubeTestGrid;
                                    currentMostBlocks = cubeTestGrid.BlocksCount;
                                }
                            }
                        }
                    }

                    Vector3D toVec = Vector3D.Normalize(grid.GetPhysicalGroupAABB().Center - currentCamera.WorldMatrix.Translation);
                    Vector3D forVec = currentCamera.WorldMatrix.Forward;

                    var angle = AngleBetweenVectors(toVec , forVec , currentCamera.WorldMatrix.Up);
                    //DEBUG
                    //MyAPIGateway.Utilities.ShowNotification($"Angle: {angle}", 1, "White");
                    if (angle <= currentSmallestAngle)
                    {
                        targetGrid = grid;
                        currentSmallestAngle = angle;
                    }
                }

                if (targetGrid != null)
                {
                    EntVis entVis = new EntVis(targetGrid , -0.12 , 0.03 , 1.1);
                    allVis.Add(entVis);
                    viewState = ViewState.Locked;
                }
                else
                {
                    viewState = ViewState.GoToIdle;
                }
            }

            if (viewState == ViewState.DoubleSearching)
            {
                allVis.Clear();
                searchEnts.Clear();

                BoundingSphereD sphere = new BoundingSphereD(currentCamera.WorldMatrix.Translation , maxRange);
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere , searchEnts);

                MyCubeGrid targetGrid = null;
                double currentSmallestAngle = maxAngleTolerance;
                foreach (var ent in searchEnts)
                {
                    MyCubeGrid grid = ent as MyCubeGrid;
                    if (grid?.Physics == null)
                    {
                        continue;
                    }

                    var group = MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Physical , grid);
                    var gridList = new List<IMyCubeGrid>();

                    if (group != null)
                    {
                        group.GetGrids(gridList);
                        if (gridList.Count > 1)
                        {
                            var currentMostBlocks = 0;
                            foreach (var testGrid in gridList)
                            {
                                MyCubeGrid cubeTestGrid = testGrid as MyCubeGrid;
                                if (cubeTestGrid.BlocksCount > currentMostBlocks)
                                {
                                    grid = cubeTestGrid;
                                    currentMostBlocks = cubeTestGrid.BlocksCount;
                                }
                            }
                        }
                    }

                    Vector3D toVec = Vector3D.Normalize(grid.GetPhysicalGroupAABB().Center - currentCamera.WorldMatrix.Translation);
                    Vector3D forVec = currentCamera.WorldMatrix.Forward;

                    var angle = AngleBetweenVectors(toVec , forVec , currentCamera.WorldMatrix.Up);
                    //DEBUG
                    //MyAPIGateway.Utilities.ShowNotification($"Angle: {angle}", 1, "White");
                    if (angle <= currentSmallestAngle)
                    {
                        targetGrid = grid;
                        currentSmallestAngle = angle;
                    }
                }
                MyEntity controlEnt2 = null;
                if (MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity is IMyCockpit)
                {
                    IMyCockpit cockpit = MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity as IMyCockpit;
                    controlEnt2 = cockpit.CubeGrid as MyEntity;
                }

                if (controlEnt2 != null && wcAPI != null)
                {
                    var ent = wcAPI.GetAiFocus(controlEnt2 , 0);
                    if (ent != null)
                    {
                        MyCubeGrid cGrid = ent as MyCubeGrid;
                        if (cGrid != null && cGrid.Physics != null)
                        {
                            EntVis entVis2 = new EntVis(cGrid , 0.10 , 0.065 , 0.5);
                            allVis.Add(entVis2);
                            //MyAPIGateway.Utilities.ShowNotification($"added: {entVis2}", 1000, "White");
                            //viewState = ViewState.Locked;                
                        }
                    }
                }
                if (targetGrid != null)
                {
                    EntVis entVis = new EntVis(targetGrid , -0.13 , 0.065 , 0.5);
                    allVis.Add(entVis);
                    //MyAPIGateway.Utilities.ShowNotification($"added: {entVis}", 1000, "White");
                }
                if (allVis.Count > 0)
                {
                    viewState = ViewState.Locked;
                }
                else
                {
                    viewState = ViewState.GoToIdle;
                }
            }

            if (viewState == ViewState.SearchingAll)
            {
                searchEnts.Clear();

                BoundingSphereD sphere = new BoundingSphereD(currentCamera.WorldMatrix.Translation , maxRange);
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere , searchEnts);

                List<MyCubeGrid> leftGrids = new List<MyCubeGrid>();
                List<MyCubeGrid> rightGrids = new List<MyCubeGrid>();
                foreach (var ent in searchEnts)
                {
                    MyCubeGrid grid = ent as MyCubeGrid;
                    if (grid?.Physics == null || grid.IsStatic || grid.BlocksCount < 100)
                    {
                        continue;
                    }


                    var group = MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Physical , grid);
                    var gridList = new List<IMyCubeGrid>();

                    if (group != null)
                    {
                        group.GetGrids(gridList);
                        if (gridList.Count > 1)
                        {
                            var currentMostBlocks = 0;
                            foreach (var testGrid in gridList)
                            {
                                MyCubeGrid cubeTestGrid = testGrid as MyCubeGrid;
                                if (cubeTestGrid.BlocksCount > currentMostBlocks)
                                {
                                    grid = cubeTestGrid;
                                    currentMostBlocks = cubeTestGrid.BlocksCount;
                                }
                            }
                        }
                    }

                    if (leftGrids.Contains(grid) || rightGrids.Contains(grid))
                    {
                        continue;
                    }

                    Vector3D toVec = Vector3D.Normalize(grid.GetPhysicalGroupAABB().Center - currentCamera.WorldMatrix.Translation);
                    Vector3D forVec = currentCamera.WorldMatrix.Right;

                    double angle = toVec.Dot(forVec);
                    if (angle < 0)
                    {
                        leftGrids.Add(grid);
                    }
                    else
                    {
                        rightGrids.Add(grid);
                    }
                }

                double leftXOffset = -0.13;
                double leftYOffset = -0.06;
                foreach (var leftGrid in leftGrids)
                {
                    EntVis leftEntVis = new EntVis(leftGrid , leftXOffset , leftYOffset , -1.1);
                    allVis.Add(leftEntVis);
                    leftYOffset += 0.03;
                }

                double rightXOffset = 0.13;
                double rightYOffset = -0.06;
                foreach (var rightGrid in rightGrids)
                {
                    EntVis rightEntVis = new EntVis(rightGrid , rightXOffset , rightYOffset , 1.1);
                    allVis.Add(rightEntVis);
                    rightYOffset += 0.03;
                }

                if (allVis.Count > 0)
                {
                    viewState = ViewState.Locked;
                }
                else
                {
                    viewState = ViewState.GoToIdle;
                }
            }

            if (viewState == ViewState.SearchingWC)
            {
                
                MyEntity controlEnt = null;
                if (MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity is IMyCockpit)
                {
                    IMyCockpit cockpit = MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity as IMyCockpit;
                    controlEnt = cockpit.CubeGrid as MyEntity;
                }

                if (controlEnt != null && wcAPI != null)
                {
                    var ent = wcAPI.GetAiFocus(controlEnt , 0);
                    if (ent != null)
                    { 
                        MyCubeGrid cGrid = ent as MyCubeGrid;
                        if (cGrid != null && cGrid.Physics != null)
                        {
                            EntVis entVis = new EntVis(cGrid , 0.12 , 0.03 , 1.1);
                            allVis.Add(entVis);
                            viewState = ViewState.Locked;
                        }
                        else
                        {
                            viewState = ViewState.GoToIdle;
                        }
                    }
                    else
                    {
                        viewState = ViewState.GoToIdle;
                    }
                }
                else
                {
                    viewState = ViewState.GoToIdle;
                }
            }

            if (viewState == ViewState.Locked)
            {
                //Logic in Draw
            }

            if (viewState == ViewState.GoToIdle)
            {
                foreach (var entVis in allVis)
                {
                    entVis.Close();
                }

                allVis.Clear();
                viewState = ViewState.Idle;
            }

            if (viewState == ViewState.GoToIdleWC)
            {
                foreach (var entVis in allVis)
                {
                    entVis.Close();
                }

                allVis.Clear();
                viewState = ViewState.SearchingWC;
            }
            //MyAPIGateway.Utilities.ShowNotification(viewState.ToString() , 1 , "white");
            //display viewstate
            timer++;
        }

        public override void Draw()
        {
            
                if (MyAPIGateway.Utilities.IsDedicated)
                {
                    return;
                }
            
            
                IMyCharacter charac = MyAPIGateway.Session.Player?.Character; //No player character - return
                if (charac == null)
                {
                    return;
                }
            
                IMyCamera currentCamera = MyAPIGateway.Session.Camera;
                if (currentCamera == null)
                {
                    return;
                }
           
            
            if (viewState == ViewState.Locked)
            {
                    //DEBUG
                    //MyAPIGateway.Utilities.ShowNotification($"Current locked grids: {allVis.Count}", 1, "White");

                    for (int i = allVis.Count - 1 ; i >= 0 ; i--)
                    {

                        allVis[ i ].Update();
                        if (allVis[ i ].isClosed)
                        {
                            allVis.RemoveAt(i);
                        }
                    }

                    if (allVis.Count == 0)
                    {
                        viewState = ViewState.GoToIdle;
                    }
            }
        }

        private void NetworkHandler(ushort arg1, byte[] arg2, ulong incomingSteamID, bool arg4)
        {
            
                Packet packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(arg2);
                if (packet != null)
                {
                    if (MyAPIGateway.Session.IsServer)
                    {
                        UpdateGridPacket updateGridPacket = packet as UpdateGridPacket;
                        if (updateGridPacket != null)
                        {
                            UpdateServerTracker(incomingSteamID, updateGridPacket);
                        }
                    }
                }
            
        }

        private void FeedbackHandler(ushort arg1 , byte[ ] arg2 , ulong arg3 , bool arg4)
        {

            Packet packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(arg2);
            if (packet != null)
            {
                FeedbackDamagePacket feedbackDamagePacket = packet as FeedbackDamagePacket;
                if (feedbackDamagePacket != null)
                {
                    foreach (var entVis in allVis)
                    {
                        if (entVis.realGrid?.EntityId == feedbackDamagePacket.entityId)
                        {
                            entVis.BlockRemoved(feedbackDamagePacket.position);
                        }
                    }
                }
            }

        }

        private void UpdateServerTracker(ulong steamID , UpdateGridPacket updateGridPacket)
        {
            if (updateGridPacket.regUpdateType == RegUpdateType.Add)
            {
                if (serverTracker.ContainsKey(steamID))
                {
                    foreach (var entId in updateGridPacket.entityIds)
                    {
                        IMyCubeGrid cubeGrid = MyAPIGateway.Entities.GetEntityById(entId) as IMyCubeGrid;
                        if (cubeGrid != null)
                        {
                            cubeGrid.OnBlockRemoved += ServerBlockRemoved;
                            serverTracker[ steamID ].Add(cubeGrid);
                        }
                    }
                }
                else
                {
                    List<IMyCubeGrid> gridTracker = new List<IMyCubeGrid>();
                    foreach (var entId in updateGridPacket.entityIds)
                    {
                        IMyCubeGrid cubeGrid = MyAPIGateway.Entities.GetEntityById(entId) as IMyCubeGrid;
                        if (cubeGrid != null)
                        {
                            cubeGrid.OnBlockRemoved += ServerBlockRemoved;
                            gridTracker.Add(cubeGrid);
                        }
                    }

                    serverTracker.Add(steamID , gridTracker);
                }
            }

            if (updateGridPacket.regUpdateType == RegUpdateType.Remove)
            {
                if (serverTracker.ContainsKey(steamID))
                {
                    foreach (var entId in updateGridPacket.entityIds)
                    {
                        IMyCubeGrid cubeGrid = MyAPIGateway.Entities.GetEntityById(entId) as IMyCubeGrid;
                        if (cubeGrid != null)
                        {
                            cubeGrid.OnBlockRemoved -= ServerBlockRemoved;
                            serverTracker[ steamID ].Remove(cubeGrid);
                        }
                    }
                }
            }

        }

        private void ServerBlockRemoved(IMySlimBlock obj)
        {

            var dmgGrid = obj.CubeGrid;
            foreach (var steamID in serverTracker.Keys)
            {
                if (serverTracker[ steamID ] != null && serverTracker[ steamID ].Count > 0)
                {
                    foreach (var checkGrid in serverTracker[ steamID ])
                    {
                        if (checkGrid.EntityId == dmgGrid.EntityId)
                        {
                            FeedbackDamagePacket feedbackDamagePacket = new FeedbackDamagePacket(dmgGrid.EntityId , obj.Position);
                            var byteArray = MyAPIGateway.Utilities.SerializeToBinary(feedbackDamagePacket);
                            MyAPIGateway.Multiplayer.SendMessageTo(feedbackNetID , byteArray , steamID);
                            break;
                        }
                    }
                }
            }

        }

        public double AngleBetweenVectors(Vector3D vectorA , Vector3D vectorB , Vector3D planeNormal , bool useNegative = false)
        {

            vectorA = Vector3D.Normalize(vectorA);
            vectorB = Vector3D.Normalize(vectorB);
            planeNormal = Vector3D.Normalize(planeNormal);

            PlaneD plane = new PlaneD(Vector3D.Zero , planeNormal);
            double angle = MyUtils.GetAngleBetweenVectors(vectorA , vectorB);
            double sign = Vector3D.Dot(plane.Normal , vectorB);

            if (useNegative)
            {
                if (sign < 0)
                {
                    angle = -angle;
                }
            }

            return angle;

        }

        private bool ValidInput()
        {
            
            if (MyAPIGateway.Session.CameraController != null && !MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible
                && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.None)
            {
                return true;
            }
            return false;
                
        }
        private bool IsAdmin(IMyPlayer sender)
        { 
            if (sender == null)
            {
                return false;
            }

            if (sender.PromoteLevel == MyPromoteLevel.Admin || sender.PromoteLevel == MyPromoteLevel.Owner)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        protected override void UnloadData()
        {
            foreach (var entVis in allVis)
            {
                entVis.Close();
            }

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(netID, NetworkHandler);
            }

            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(feedbackNetID, FeedbackHandler);

            if(wcAPI != null)
            {
                wcAPI.Unload();
            }
        }
    }
}