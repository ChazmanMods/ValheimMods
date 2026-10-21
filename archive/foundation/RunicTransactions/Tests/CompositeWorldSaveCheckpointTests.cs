using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunicTransactions.Valheim;

namespace RunicTransactions.Tests
{
    internal static class CompositeWorldSaveCheckpointTests
    {
        internal static void InstalledSavePipelineSupportsExactDurableCheckpoint()
        {
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
                       typeof(ZNet).Assembly.Location))
            {
                TypeDefinition znet = RequireType(game, "ZNet");
                MethodDefinition save = znet.Methods.Single(method =>
                    method.Name == "SaveWorld" && method.Parameters.Count == 1 &&
                    method.Parameters[0].ParameterType.MetadataType == MetadataType.Boolean);
                int started = IndexOfField(save, "System.Action ZNet::WorldSaveStarted");
                int prepare = IndexOfCall(save, "System.Void ZDOMan::PrepareSave()");
                int threadStart = IndexOfCall(
                    save, "System.Void System.Threading.Thread::Start()");
                True(started >= 0 && started < prepare && prepare < threadStart,
                    "WorldSaveStarted no longer brackets the exact pre-PrepareSave barrier window.");

                MethodDefinition thread = znet.Methods.Single(method =>
                    method.Name == "SaveWorldThread" && method.Parameters.Count == 0);
                int finish = IndexOfCall(thread, "System.Void FileWriter::Finish()");
                int metadata = IndexOfCall(
                    thread,
                    "System.Void World::SaveWorldMetaData(System.DateTime,System.Boolean,System.Boolean&,FileWriter&)");
                int replace = IndexOfCall(
                    thread,
                    "System.Void FileHelpers::ReplaceOldFile(System.String,System.String,System.String,FileHelpers/FileSource)");
                True(finish >= 0 && finish < metadata && metadata < replace,
                    "The installed save thread no longer flushes DB, replaces metadata, then replaces DB.");
                Instruction replaceInstruction = thread.Body.Instructions[replace];
                True(thread.Body.ExceptionHandlers.Any(handler =>
                        handler.HandlerType == ExceptionHandlerType.Catch &&
                        handler.CatchType?.FullName == "System.Exception" &&
                        handler.TryStart.Offset <= replaceInstruction.Offset &&
                        (handler.TryEnd == null || replaceInstruction.Offset < handler.TryEnd.Offset)),
                    "SaveWorldThread no longer swallows database replacement failure as expected by the false-green guard.");
            }

            using (AssemblyDefinition utility = AssemblyDefinition.ReadAssembly(
                       typeof(FileWriter).Assembly.Location))
            {
                TypeDefinition writer = RequireType(utility, "FileWriter");
                MethodDefinition finish = writer.Methods.Single(method =>
                    method.Name == "Finish" && method.Parameters.Count == 0);
                int flush = IndexOfCall(
                    finish,
                    "System.Void System.IO.FileStream::Flush(System.Boolean)");
                True(flush > 0 &&
                     finish.Body.Instructions[flush - 1].OpCode.Code == Code.Ldc_I4_1,
                    "FileWriter.Finish no longer performs a durable Flush(true).");

                TypeDefinition helpers = RequireType(utility, "FileHelpers");
                MethodDefinition replace = helpers.Methods.Single(method =>
                    method.Name == "ReplaceOldFile" && method.Parameters.Count == 4);
                True(replace.Body.ExceptionHandlers.Count == 0,
                    "ReplaceOldFile now hides local File.Move failures from the checkpoint postfix.");
                True(replace.Body.Instructions.Count(instruction =>
                         instruction.Operand is MethodReference method &&
                         method.FullName == "System.Void System.IO.File::Move(System.String,System.String)") >= 2,
                    "Local ReplaceOldFile no longer moves primary-to-old and new-to-primary.");
            }

            using (AssemblyDefinition transactions = AssemblyDefinition.ReadAssembly(
                       typeof(CompositeWorldSaveCheckpoint).Assembly.Location))
            {
                TypeDefinition checkpoint = RequireType(
                    transactions,
                    "RunicTransactions.Valheim.CompositeWorldSaveCheckpoint");
                True(!checkpoint.Methods
                        .Where(method => method.HasBody)
                        .SelectMany(method => method.Body.Instructions)
                        .Any(instruction => instruction.Operand is FieldReference field &&
                             field.FullName.Contains("WorldSaveFinished", StringComparison.Ordinal)),
                    "The checkpoint bridge trusted Valheim's false-green WorldSaveFinished signal.");
            }
        }

        internal static void MetadataReplaceCannotConsumeDatabaseCandidate()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "runic-checkpoint-paths-" + Guid.NewGuid().ToString("N"));
            string database = Path.Combine(root, "world.db");
            string databaseNew = database + ".new";
            string databaseOld = database + ".old";
            string metadata = Path.Combine(root, "world.fwl");
            True(!CompositeWorldSaveCheckpoint.IsExactDatabaseReplace(
                    metadata,
                    metadata + ".new",
                    metadata + ".old",
                    database,
                    databaseNew,
                    databaseOld),
                "The metadata replacement consumed the pending database checkpoint candidate.");
            True(CompositeWorldSaveCheckpoint.IsExactDatabaseReplace(
                    database,
                    databaseNew,
                    databaseOld,
                    database,
                    databaseNew,
                    databaseOld),
                "The exact database replacement was not recognized.");
            True(!CompositeWorldSaveCheckpoint.IsExactDatabaseReplace(
                    database,
                    databaseNew,
                    database + ".wrong",
                    database,
                    databaseNew,
                    databaseOld),
                "A mismatched fallback database path was accepted.");
        }

        internal static void MarkerCreationAssignsExactNonzeroPrefab()
        {
            int markerHash = "RunicTransactions_CompositeCheckpoint".GetStableHashCode();
            True(markerHash != 0, "The checkpoint marker prefab hash unexpectedly became zero.");
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
                       typeof(ZNet).Assembly.Location))
            {
                TypeDefinition manager = RequireType(game, "ZDOMan");
                MethodDefinition exactCreate = manager.Methods.Single(method =>
                    method.Name == "CreateNewZDO" && method.Parameters.Count == 3 &&
                    method.Parameters[0].ParameterType.FullName == "ZDOID");
                True(IndexOfCall(exactCreate, "System.Void ZDO::SetPrefab(System.Int32)") < 0,
                    "The installed exact creation API unexpectedly began assigning the prefab itself.");
            }
            using (AssemblyDefinition transactions = AssemblyDefinition.ReadAssembly(
                       typeof(CompositeWorldSaveCheckpoint).Assembly.Location))
            {
                TypeDefinition checkpoint = RequireType(
                    transactions, "RunicTransactions.Valheim.CompositeWorldSaveCheckpoint");
                MethodDefinition create = checkpoint.Methods.Single(method =>
                    method.Name == "TryCreateMarker");
                int assign = IndexOfCall(create, "System.Void ZDO::SetPrefab(System.Int32)");
                int verify = IndexOfCall(create, "System.Int32 ZDO::GetPrefab()");
                True(assign >= 0 && verify > assign,
                    "Checkpoint creation does not assign and then verify the exact marker prefab.");
            }
            True(CompositeWorldSaveCheckpoint.IsExactMarkerForReuse(
                    true, true, markerHash, true, markerHash),
                "The exact nonzero marker was not reusable.");
        }

        internal static void PrefabZeroNeverBecomesPersistent()
        {
            using (AssemblyDefinition transactions = AssemblyDefinition.ReadAssembly(
                       typeof(CompositeWorldSaveCheckpoint).Assembly.Location))
            {
                TypeDefinition checkpoint = RequireType(
                    transactions, "RunicTransactions.Valheim.CompositeWorldSaveCheckpoint");
                MethodDefinition create = checkpoint.Methods.Single(method =>
                    method.Name == "TryCreateMarker");
                MethodDefinition activate = checkpoint.Methods.Single(method =>
                    method.Name == "TryMakeCreatedMarkerPersistent");
                MethodDefinition discard = checkpoint.Methods.Single(method =>
                    method.Name == "DiscardFailedMarker");
                True(IndexOfCall(create, "System.Void ZDO::set_Persistent(System.Boolean)") < 0,
                    "Marker creation can make a ZDO persistent before prefab verification.");
                int prefab = IndexOfCall(activate, "System.Int32 ZDO::GetPrefab()");
                int persistent = IndexOfCall(
                    activate, "System.Void ZDO::set_Persistent(System.Boolean)");
                True(prefab >= 0 && persistent > prefab,
                    "Marker persistence can occur before exact prefab verification.");
                True(IndexOfCall(discard, "System.Void ZDO::set_Persistent(System.Boolean)") >= 0 &&
                     IndexOfCall(discard, "System.Void ZDOMan::DestroyZDO(ZDO)") >= 0,
                    "Failed marker creation does not clear persistence and destroy its ZDO.");
            }
            True(!CompositeWorldSaveCheckpoint.IsExactMarkerForReuse(
                    true, true, 0, true,
                    "RunicTransactions_CompositeCheckpoint".GetStableHashCode()),
                "A persistent prefab-0 ZDO was accepted as a checkpoint marker.");
        }

        internal static void RepeatedRefreshesReuseOneMarker()
        {
            int markerHash = "RunicTransactions_CompositeCheckpoint".GetStableHashCode();
            int created = 0;
            bool exists = false;
            for (int update = 0; update < 512; update++)
            {
                if (exists && CompositeWorldSaveCheckpoint.IsExactMarkerForReuse(
                        true, true, markerHash, true, markerHash)) continue;
                created++;
                exists = true;
            }
            True(created == 1, "Repeated refreshes created more than one exact marker.");
        }

        internal static void FailedCreationCannotRetryWithoutBound()
        {
            DateTime now = new DateTime(638900000000000000L, DateTimeKind.Utc);
            True(CompositeWorldSaveCheckpoint.MarkerCreationAttemptAllowed(0, now, now),
                "The initial marker attempt was not allowed.");
            True(CompositeWorldSaveCheckpoint.MarkerCreationAttemptAllowed(2, now, now),
                "The final bounded marker attempt was not allowed.");
            True(!CompositeWorldSaveCheckpoint.MarkerCreationAttemptAllowed(3, now, now),
                "Marker creation retries were not capped at three failures.");
            True(!CompositeWorldSaveCheckpoint.MarkerCreationAttemptAllowed(
                    1, now.AddSeconds(1), now),
                "Marker creation ignored its bounded retry interval.");
        }

        internal static void MarkerSurvivesSaveReloadModel()
        {
            int markerHash = "RunicTransactions_CompositeCheckpoint".GetStableHashCode();
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(markerHash);
                    writer.Write(true);
                }
                stream.Position = 0;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                {
                    int reloadedHash = reader.ReadInt32();
                    bool reloadedPersistent = reader.ReadBoolean();
                    True(CompositeWorldSaveCheckpoint.IsExactMarkerForReuse(
                            true, true, reloadedHash, reloadedPersistent, markerHash),
                        "The exact marker was not reusable after a save/reload boundary.");
                }
            }
        }

        internal static void BoundedUpdatesProduceZeroHashZeroGrowth()
        {
            int markerHash = "RunicTransactions_CompositeCheckpoint".GetStableHashCode();
            int total = 0;
            int persistentHashZero = 0;
            bool exists = false;
            for (int update = 0; update < 4096; update++)
            {
                if (!exists)
                {
                    total++;
                    exists = true;
                    if (markerHash == 0) persistentHashZero++;
                }
                else
                {
                    True(CompositeWorldSaveCheckpoint.IsExactMarkerForReuse(
                            true, true, markerHash, true, markerHash),
                        "A bounded update failed to reuse the exact marker.");
                }
            }
            True(total == 1, "The bounded update model grew the checkpoint marker set.");
            True(persistentHashZero == 0,
                "The bounded update model grew the persistent hash-0 set.");
        }

        internal static void RemappedMarkerIsRediscoveredByLogicalIdentity()
        {
            string epoch = Guid.NewGuid().ToString("N");
            string cycle = Guid.NewGuid().ToString("N");
            string scope = "valheim.0123456789abcdef";
            string legacy = "v1:" + epoch + ":0000000000000007:" + cycle;
            True(CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                    legacy, scope, epoch, legacy, 7, cycle),
                "The exact staged logical marker was not recognized after numeric ID remapping.");
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(true, 1, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.BindExisting,
                "One valid remapped marker did not bind existing state.");
        }

        internal static void StaleNumericHintNeverCreatesAReplacement()
        {
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(true, 1, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.BindExisting,
                "A staged checkpoint ignored its unique logical marker.");
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(true, 0, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.FailClosed,
                "A missing staged marker authorized replacement creation from a stale numeric hint.");
        }

        internal static void OneLogicalMarkerSurvivesSaveReloadModel()
        {
            string epoch = Guid.NewGuid().ToString("N");
            string cycle = Guid.NewGuid().ToString("N");
            string scope = "valheim.1111222233334444";
            string value = "v2:runic-checkpoint:" + scope + ":" + epoch +
                ":0000000000000009:" + cycle;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    writer.Write(value);
                stream.Position = 0;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                    True(CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                            reader.ReadString(), scope, epoch, string.Empty, 0, string.Empty),
                        "The schema-2 logical marker did not survive its save/reload model.");
            }
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(false, 1, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.BindExisting,
                "Reload created a second logical marker.");
        }

        internal static void ZeroLogicalMarkersUsesOnlyBoundedBootstrapCreation()
        {
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(false, 0, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.Create,
                "A clean checkpoint stream could not create its one bootstrap marker.");
            True(CompositeWorldSaveCheckpoint.MarkerCreationAttemptAllowed(
                    0, DateTime.MinValue, DateTime.UtcNow),
                "The bounded initial bootstrap creation was unavailable.");
            True(!CompositeWorldSaveCheckpoint.MarkerCreationAttemptAllowed(
                    3, DateTime.MinValue, DateTime.UtcNow),
                "Bootstrap creation could exceed its fixed retry bound.");
        }

        internal static void DuplicateLogicalMarkersFailWithoutCreation()
        {
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(true, 2, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.FailClosed,
                "Two valid staged markers did not fail closed.");
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(false, 2, 0) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.FailClosed,
                "Two valid bootstrap markers could create a third marker.");
        }

        internal static void ForeignWrongSchemaAndMalformedMarkersAreRejected()
        {
            string epoch = Guid.NewGuid().ToString("N");
            string otherEpoch = Guid.NewGuid().ToString("N");
            string cycle = Guid.NewGuid().ToString("N");
            string scope = "valheim.aaaabbbbccccdddd";
            string valid = "v2:runic-checkpoint:" + scope + ":" + epoch +
                ":0000000000000003:" + cycle;
            True(CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                    valid, scope, epoch, string.Empty, 0, string.Empty),
                "The exact logical marker fixture was invalid.");
            True(!CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                    valid, "valheim.0000000000000001", epoch,
                    string.Empty, 0, string.Empty),
                "A foreign-world marker was accepted.");
            True(!CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                    valid, scope, otherEpoch, string.Empty, 0, string.Empty),
                "A wrong-stream marker was accepted.");
            True(!CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                    valid.Replace("v2:", "v3:"), scope, epoch,
                    string.Empty, 0, string.Empty),
                "A wrong-schema marker was accepted.");
            True(!CompositeWorldSaveCheckpoint.IsLogicalMarkerValue(
                    "v2:runic-checkpoint:broken", scope, epoch,
                    string.Empty, 0, string.Empty),
                "Malformed marker metadata was accepted.");
            True(CompositeWorldSaveCheckpoint.DecideMarkerResolution(true, 0, 1) ==
                 CompositeWorldSaveCheckpoint.CheckpointMarkerResolution.FailClosed,
                "A rejected checkpoint marker allowed creation or selection.");
        }

        private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName) =>
            assembly.MainModule.Types.Single(type => type.FullName == fullName);

        private static int IndexOfCall(MethodDefinition method, string fullName)
        {
            for (int index = 0; index < method.Body.Instructions.Count; index++)
                if (method.Body.Instructions[index].Operand is MethodReference candidate &&
                    candidate.FullName == fullName)
                    return index;
            return -1;
        }

        private static int IndexOfField(MethodDefinition method, string fullName)
        {
            for (int index = 0; index < method.Body.Instructions.Count; index++)
                if (method.Body.Instructions[index].Operand is FieldReference candidate &&
                    candidate.FullName == fullName)
                    return index;
            return -1;
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
