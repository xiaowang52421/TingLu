using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using TingLu.Contracts;

namespace TingLu.Persistence
{
    [DataContract]
    internal sealed class StreetSaveEnvelopeDto
    {
        [DataMember(IsRequired = true, Order = 0)] public int schema;
        [DataMember(IsRequired = true, Order = 1)] public string kind;
        [DataMember(IsRequired = true, Order = 2)] public string contract;
        [DataMember(IsRequired = true, Order = 3)] public string levelId;
        [DataMember(IsRequired = true, Order = 4)] public string checksum;
        [DataMember(IsRequired = true, Order = 5)] public string payload;
    }

    [DataContract]
    internal sealed class StreetSavePayloadDto
    {
        [DataMember(IsRequired = true, Order = 0)] public string levelId;
        [DataMember(IsRequired = true, Order = 1)] public string checkpointId;
        [DataMember(IsRequired = true, Order = 2)] public StreetPlayerDto player;
        [DataMember(IsRequired = true, Order = 3)] public StreetPlayerDto checkpointPlayer;
        [DataMember(IsRequired = true, Order = 4)] public StreetTrafficDto traffic;
        [DataMember(IsRequired = true, Order = 5)] public StreetTrafficDto checkpointTraffic;
        [DataMember(IsRequired = true, Order = 6)] public StreetMemoryDto memory;
        [DataMember(IsRequired = true, Order = 7)] public int collisions;
        [DataMember(IsRequired = true, Order = 8)] public int generation;
        [DataMember(IsRequired = true, Order = 9)] public string[] activatedCheckpoints;
        [DataMember(IsRequired = true, Order = 10)] public bool finished;
    }

    [DataContract]
    internal sealed class StreetPlayerDto
    {
        [DataMember(IsRequired = true, Order = 0)] public int mainState;
        [DataMember(IsRequired = true, Order = 1)] public int mode;
        [DataMember(IsRequired = true, Order = 2)] public StreetPointDto position;
        [DataMember(IsRequired = true, Order = 3)] public StreetPointDto facing;
        [DataMember(IsRequired = true, Order = 4)] public float courage;
        [DataMember(IsRequired = true, Order = 5)] public float focus;
        [DataMember(IsRequired = true, Order = 6)] public double caneReadyAt;
        [DataMember(IsRequired = true, Order = 7)] public double stunnedUntil;
        [DataMember(IsRequired = true, Order = 8)] public double confusedUntil;
    }

    [DataContract]
    internal sealed class StreetTrafficDto
    {
        [DataMember(IsRequired = true, Order = 0)] public double time;
        [DataMember(IsRequired = true, Order = 1)] public string[] objectIds;
        [DataMember(IsRequired = true, Order = 2)] public double[] distances;
    }

    [DataContract]
    internal sealed class StreetMemoryDto
    {
        [DataMember(IsRequired = true, Order = 0)] public StreetSurfaceMemoryDto[] surfaces;
        [DataMember(IsRequired = true, Order = 1)] public StreetObjectMemoryDto[] objects;
    }

    [DataContract]
    internal sealed class StreetSurfaceMemoryDto
    {
        [DataMember(IsRequired = true, Order = 0)] public string materialId;
        [DataMember(IsRequired = true, Order = 1)] public StreetPointDto[] polygon;
        [DataMember(IsRequired = true, Order = 2)] public double observedAt;
        [DataMember(IsRequired = true, Order = 3)] public double currentUntil;
        // Optional for schema-1 saves written before texture orientation was persisted.
        [DataMember(IsRequired = false, Order = 4)] public StreetPointDto textureAxis;
        [DataMember(IsRequired = false, Order = 5)] public float heightBase;
        [DataMember(IsRequired = false, Order = 6)] public float heightSlopeX;
        [DataMember(IsRequired = false, Order = 7)] public float heightSlopeY;
    }

    [DataContract]
    internal sealed class StreetObjectMemoryDto
    {
        [DataMember(IsRequired = true, Order = 0)] public string objectId;
        [DataMember(IsRequired = true, Order = 1)] public string materialId;
        [DataMember(IsRequired = true, Order = 2)] public int kind;
        [DataMember(IsRequired = true, Order = 3)] public StreetSegmentDto[] contour;
        [DataMember(IsRequired = true, Order = 4)] public float height;
        [DataMember(IsRequired = true, Order = 5)] public double observedAt;
        [DataMember(IsRequired = true, Order = 6)] public double currentUntil;
    }

    [DataContract]
    internal sealed class StreetPointDto
    {
        [DataMember(IsRequired = true, Order = 0)] public float x;
        [DataMember(IsRequired = true, Order = 1)] public float y;
    }

    [DataContract]
    internal sealed class StreetSegmentDto
    {
        [DataMember(IsRequired = true, Order = 0)] public StreetPointDto a;
        [DataMember(IsRequired = true, Order = 1)] public StreetPointDto b;
    }

    /// <summary>
    /// Stores the frozen street save contract in a namespace isolated from legacy saves.
    /// Only value snapshots are encoded; active acoustic sources have no DTO representation.
    /// </summary>
    public sealed class StreetSaveService : IStreetSaveService
    {
        private const int Schema = 1;
        private const string Kind = "tinglu.street";
        private const string CurrentContract = StreetContract.Version;
        private readonly string root;
        private readonly object gate = new object();

        public StreetSaveService(string persistentRoot, string saveNamespace)
        {
            if (string.IsNullOrWhiteSpace(persistentRoot)) throw new ArgumentException("Persistent root is required", "persistentRoot");
            ValidateNamespace(saveNamespace);
            root = Path.GetFullPath(Path.Combine(persistentRoot, saveNamespace));
            Directory.CreateDirectory(root);
        }

        public SaveStatus Save(StreetSaveData value)
        {
            if (value == null) return SaveStatus.IoFailure;
            lock (gate)
            {
                string temporary = null;
                try
                {
                    Directory.CreateDirectory(root);
                    string path = SavePath(value.LevelId);
                    string backupPath = path + ".bak";
                    ReadResult primary = Inspect(path, value.LevelId);
                    ReadResult backup = Inspect(backupPath, value.LevelId);

                    if (primary.Status == SaveStatus.FutureVersion || backup.Status == SaveStatus.FutureVersion)
                        return SaveStatus.FutureVersion;
                    if (primary.Status == SaveStatus.IoFailure || backup.Status == SaveStatus.IoFailure)
                        return SaveStatus.IoFailure;
                    if (primary.Status == SaveStatus.Corrupt && backup.Status != SaveStatus.Success)
                        return SaveStatus.Corrupt;
                    if (primary.Status == SaveStatus.Missing && backup.Status == SaveStatus.Corrupt)
                        return SaveStatus.Corrupt;

                    StreetSavePayloadDto payloadDto = Encode(value);
                    string payload = ToJson(payloadDto);
                    var envelope = new StreetSaveEnvelopeDto
                    {
                        schema = Schema,
                        kind = Kind,
                        contract = CurrentContract,
                        levelId = value.LevelId,
                        payload = payload,
                        checksum = Hash(Schema, Kind, CurrentContract, value.LevelId, payload)
                    };
                    byte[] bytes = Encoding.UTF8.GetBytes(ToJson(envelope));
                    temporary = path + ".tmp";
                    WriteFlushed(temporary, bytes);

                    if (primary.Status == SaveStatus.Missing)
                    {
                        // A previous backup may be the only recoverable copy. Keep it intact.
                        File.Move(temporary, path);
                    }
                    else if (primary.Status == SaveStatus.Corrupt)
                    {
                        // The backup is valid, so retain the damaged primary as evidence while
                        // atomically installing the new snapshot. Never consume the good backup.
                        File.Replace(temporary, path, NextQuarantinePath(path), true);
                    }
                    else
                    {
                        if (backup.Status == SaveStatus.Corrupt)
                        {
                            // Preserve an invalid backup before rotating the valid primary into it.
                            File.Move(backupPath, NextQuarantinePath(backupPath));
                        }
                        File.Replace(temporary, path, backupPath, true);
                    }

                    temporary = null;
                    return SaveStatus.Success;
                }
                catch
                {
                    return SaveStatus.IoFailure;
                }
                finally
                {
                    if (temporary != null) TryDelete(temporary);
                }
            }
        }

        public SaveResult<StreetSaveData> Load(string levelId)
        {
            ContractGuard.Id(levelId);
            lock (gate)
            {
                string path = SavePath(levelId);
                ReadResult primary = Inspect(path, levelId);
                ReadResult backup = Inspect(path + ".bak", levelId);

                if (primary.Status == SaveStatus.FutureVersion || backup.Status == SaveStatus.FutureVersion)
                {
                    ReadResult future = primary.Status == SaveStatus.FutureVersion ? primary : backup;
                    return new SaveResult<StreetSaveData>(SaveStatus.FutureVersion, null, false, future.Error);
                }
                if (primary.Status == SaveStatus.IoFailure || backup.Status == SaveStatus.IoFailure)
                {
                    ReadResult failed = primary.Status == SaveStatus.IoFailure ? primary : backup;
                    return new SaveResult<StreetSaveData>(SaveStatus.IoFailure, null, false, failed.Error);
                }
                if (primary.Status == SaveStatus.Success)
                    return new SaveResult<StreetSaveData>(SaveStatus.Success, primary.Value);

                if (backup.Status == SaveStatus.Success)
                    return new SaveResult<StreetSaveData>(SaveStatus.Success, backup.Value, true, primary.Error);
                if (primary.Status == SaveStatus.Corrupt || backup.Status == SaveStatus.Corrupt)
                    return new SaveResult<StreetSaveData>(SaveStatus.Corrupt, null, false, JoinErrors(primary.Error, backup.Error));
                return new SaveResult<StreetSaveData>(SaveStatus.Missing, null);
            }
        }

        private ReadResult Inspect(string path, string requestedLevelId)
        {
            if (!File.Exists(path)) return ReadResult.Missing();

            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return ReadResult.Failure(SaveStatus.IoFailure, ex.Message);
            }

            StreetSaveEnvelopeDto envelope;
            try
            {
                envelope = FromJson<StreetSaveEnvelopeDto>(json);
            }
            catch (Exception ex)
            {
                return ReadResult.Failure(SaveStatus.Corrupt, "Invalid save envelope: " + ex.Message);
            }

            if (envelope == null) return ReadResult.Failure(SaveStatus.Corrupt, "Save envelope is empty");
            // Protect future and unrecognized contract data before checksum/payload handling.
            // A newer writer may use a prerelease or other contract label this version cannot parse.
            if (envelope.schema > Schema)
                return ReadResult.Failure(SaveStatus.FutureVersion, "Save was written by a newer contract");
            if (!string.IsNullOrEmpty(envelope.contract) && !IsRecognizedContract(envelope.contract))
                return ReadResult.Failure(SaveStatus.FutureVersion,
                    "Unrecognized contract version; refusing to load or overwrite this save");
            if (IsFutureContract(envelope.contract))
                return ReadResult.Failure(SaveStatus.FutureVersion, "Save was written by a newer contract");
            if (envelope.schema < Schema || IsOlderContract(envelope.contract))
                return ReadResult.Failure(SaveStatus.Corrupt, "Save was written by an unsupported older contract");
            if (envelope.schema != Schema || envelope.kind != Kind || envelope.contract != CurrentContract ||
                string.IsNullOrWhiteSpace(envelope.levelId) || string.IsNullOrEmpty(envelope.payload) ||
                string.IsNullOrEmpty(envelope.checksum))
                return ReadResult.Failure(SaveStatus.Corrupt, "Save envelope does not match this schema");
            if (!string.Equals(envelope.levelId, requestedLevelId, StringComparison.Ordinal))
                return ReadResult.Failure(SaveStatus.Corrupt, "Save level id does not match the requested level");
            if (!string.Equals(envelope.checksum, Hash(envelope.schema, envelope.kind, envelope.contract, envelope.levelId, envelope.payload), StringComparison.Ordinal))
                return ReadResult.Failure(SaveStatus.Corrupt, "Save checksum does not match");

            try
            {
                StreetSavePayloadDto dto = FromJson<StreetSavePayloadDto>(envelope.payload);
                if (dto == null || dto.levelId != envelope.levelId)
                    return ReadResult.Failure(SaveStatus.Corrupt, "Save payload level id does not match its envelope");
                return ReadResult.Valid(Decode(dto));
            }
            catch (Exception ex)
            {
                return ReadResult.Failure(SaveStatus.Corrupt, "Invalid save payload: " + ex.Message);
            }
        }

        private static StreetSavePayloadDto Encode(StreetSaveData value)
        {
            return new StreetSavePayloadDto
            {
                levelId = value.LevelId,
                checkpointId = value.CheckpointId,
                player = Encode(value.Player),
                checkpointPlayer = Encode(value.CheckpointPlayer),
                traffic = Encode(value.Traffic),
                checkpointTraffic = Encode(value.CheckpointTraffic),
                memory = Encode(value.Memory),
                collisions = value.Collisions,
                generation = value.Generation,
                activatedCheckpoints = CopyStrings(value.ActivatedCheckpoints),
                finished = value.Finished
            };
        }

        private static StreetSaveData Decode(StreetSavePayloadDto dto)
        {
            if (dto.collisions < 0 || dto.generation < 0) throw new SerializationException("Invalid attempt counters");
            if (dto.activatedCheckpoints == null) throw new SerializationException("Activated checkpoint list is missing");
            foreach (string id in dto.activatedCheckpoints) ContractGuard.Id(id);
            return new StreetSaveData(dto.levelId, dto.checkpointId, Decode(dto.player), Decode(dto.checkpointPlayer),
                Decode(dto.traffic), Decode(dto.checkpointTraffic), Decode(dto.memory), dto.collisions,
                dto.generation, dto.activatedCheckpoints, dto.finished);
        }

        private static StreetPlayerDto Encode(PlayerState value)
        {
            return new StreetPlayerDto
            {
                mainState = (int)value.MainState,
                mode = (int)value.Mode,
                position = Point(value.Position),
                facing = Point(value.Facing),
                courage = value.Courage,
                focus = value.Focus,
                caneReadyAt = value.CaneReadyAt,
                stunnedUntil = value.StunnedUntil,
                confusedUntil = value.ConfusedUntil
            };
        }

        private static PlayerState Decode(StreetPlayerDto dto)
        {
            if (dto == null || !Enum.IsDefined(typeof(PlayerMainState), dto.mainState) || !Enum.IsDefined(typeof(ActionMode), dto.mode))
                throw new SerializationException("Invalid player state");
            return new PlayerState((PlayerMainState)dto.mainState, (ActionMode)dto.mode, Point(dto.position), Point(dto.facing),
                dto.courage, dto.focus, dto.caneReadyAt, dto.stunnedUntil, dto.confusedUntil);
        }

        private static StreetTrafficDto Encode(StreetTrafficState value)
        {
            var ids = new string[value.ObjectIds.Count];
            var distances = new double[value.Distances.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = value.ObjectIds[i];
                distances[i] = value.Distances[i];
            }
            return new StreetTrafficDto { time = value.Time, objectIds = ids, distances = distances };
        }

        private static StreetTrafficState Decode(StreetTrafficDto dto)
        {
            if (dto == null || dto.objectIds == null || dto.distances == null || dto.objectIds.Length != dto.distances.Length)
                throw new SerializationException("Invalid traffic snapshot");
            for (int i = 0; i < dto.objectIds.Length; i++)
            {
                ContractGuard.Id(dto.objectIds[i]);
                if (double.IsNaN(dto.distances[i]) || double.IsInfinity(dto.distances[i]))
                    throw new SerializationException("Invalid traffic distance");
            }
            return new StreetTrafficState(dto.time, dto.objectIds, dto.distances);
        }

        private static StreetMemoryDto Encode(StreetMemoryFrame value)
        {
            var surfaces = new StreetSurfaceMemoryDto[value.Surfaces.Count];
            for (int i = 0; i < surfaces.Length; i++)
            {
                StreetKnownSurface item = value.Surfaces[i];
                var polygon = new StreetPointDto[item.Polygon.Count];
                for (int j = 0; j < polygon.Length; j++) polygon[j] = Point(item.Polygon[j]);
                surfaces[i] = new StreetSurfaceMemoryDto
                {
                    materialId = item.MaterialId,
                    polygon = polygon,
                    observedAt = item.ObservedAt,
                    currentUntil = item.CurrentUntil,
                    textureAxis = Point(item.TextureAxis),
                    heightBase = item.HeightBase,
                    heightSlopeX = item.HeightSlopeX,
                    heightSlopeY = item.HeightSlopeY
                };
            }

            var objects = new StreetObjectMemoryDto[value.Objects.Count];
            for (int i = 0; i < objects.Length; i++)
            {
                StreetKnownObject item = value.Objects[i];
                if (item.Kind != StreetKind.Permanent && item.Kind != StreetKind.Temporary)
                    throw new SerializationException("Active sources cannot be saved as geometry memories");
                var contour = new StreetSegmentDto[item.Contour.Count];
                for (int j = 0; j < contour.Length; j++)
                    contour[j] = new StreetSegmentDto { a = Point(item.Contour[j].A), b = Point(item.Contour[j].B) };
                objects[i] = new StreetObjectMemoryDto
                {
                    objectId = item.ObjectId,
                    materialId = item.MaterialId,
                    kind = (int)item.Kind,
                    contour = contour,
                    height = item.Height,
                    observedAt = item.ObservedAt,
                    currentUntil = item.CurrentUntil
                };
            }
            return new StreetMemoryDto { surfaces = surfaces, objects = objects };
        }

        private static StreetMemoryFrame Decode(StreetMemoryDto dto)
        {
            if (dto == null || dto.surfaces == null || dto.objects == null)
                throw new SerializationException("Memory snapshot is missing");
            var surfaces = new StreetKnownSurface[dto.surfaces.Length];
            for (int i = 0; i < surfaces.Length; i++)
            {
                StreetSurfaceMemoryDto item = dto.surfaces[i];
                if (item == null || item.polygon == null) throw new SerializationException("Invalid known surface");
                var polygon = new Point2[item.polygon.Length];
                for (int j = 0; j < polygon.Length; j++) polygon[j] = Point(item.polygon[j]);
                ContractGuard.Time(item.currentUntil); // Validate the stored value even though it is intentionally discarded.
                Point2 textureAxis = item.textureAxis == null
                    ? new Point2(1, 0)
                    : Point(item.textureAxis);
                surfaces[i] = new StreetKnownSurface(item.materialId, polygon, item.observedAt, 0, textureAxis,
                    item.heightBase, item.heightSlopeX, item.heightSlopeY);
            }

            var objects = new StreetKnownObject[dto.objects.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                StreetObjectMemoryDto item = dto.objects[i];
                if (item == null || item.contour == null || !Enum.IsDefined(typeof(StreetKind), item.kind))
                    throw new SerializationException("Invalid remembered object");
                var contour = new Segment2[item.contour.Length];
                for (int j = 0; j < contour.Length; j++)
                {
                    if (item.contour[j] == null) throw new SerializationException("Invalid remembered contour");
                    contour[j] = new Segment2(Point(item.contour[j].a), Point(item.contour[j].b));
                }
                StreetKind kind = (StreetKind)item.kind;
                if (kind != StreetKind.Permanent && kind != StreetKind.Temporary)
                    throw new SerializationException("Active sources cannot be loaded as geometry memories");
                ContractGuard.Time(item.currentUntil);
                objects[i] = new StreetKnownObject(item.objectId, item.materialId, kind, contour, item.height, item.observedAt, 0);
            }
            return new StreetMemoryFrame(surfaces, objects);
        }

        private string SavePath(string levelId)
        {
            return Path.Combine(root, "street-" + HashText(levelId) + ".json");
        }

        private static StreetPointDto Point(Point2 point) { return new StreetPointDto { x = point.X, y = point.Y }; }
        private static Point2 Point(StreetPointDto point)
        {
            if (point == null) throw new SerializationException("Point is missing");
            return new Point2(point.x, point.y);
        }

        private static string[] CopyStrings(System.Collections.Generic.IEnumerable<string> values)
        {
            var copy = new System.Collections.Generic.List<string>(values).ToArray();
            foreach (string value in copy) ContractGuard.Id(value);
            return copy;
        }

        private static void ValidateNamespace(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiLetterOrDigit(value[0]))
                throw new ArgumentException("Save namespace must be a single safe path segment", "saveNamespace");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                    throw new ArgumentException("Save namespace must be a single safe path segment", "saveNamespace");
            }
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        private static string NextQuarantinePath(string path)
        {
            string candidate = path + ".corrupt";
            int suffix = 1;
            while (File.Exists(candidate)) candidate = path + ".corrupt." + (suffix++).ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        private static void WriteFlushed(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string ToJson<T>(T item)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, item);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T FromJson<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)serializer.ReadObject(stream);
        }

        private static string Hash(int schema, string kind, string contract, string levelId, string payload)
        {
            var canonical = new StringBuilder();
            canonical.Append(schema.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendField(canonical, kind);
            AppendField(canonical, contract);
            AppendField(canonical, levelId);
            AppendField(canonical, payload);
            return HashText(canonical.ToString());
        }

        private static void AppendField(StringBuilder target, string value)
        {
            target.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
        }

        private static string HashText(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static bool IsFutureContract(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            int currentMajor, currentMinor, currentPatch, major, minor, patch;
            if (!TryVersion(CurrentContract, out currentMajor, out currentMinor, out currentPatch) ||
                !TryVersion(value, out major, out minor, out patch)) return false;
            if (major != currentMajor) return major > currentMajor;
            if (minor != currentMinor) return minor > currentMinor;
            return patch > currentPatch;
        }

        private static bool IsRecognizedContract(string value)
        {
            int major, minor, patch;
            return TryVersion(value, out major, out minor, out patch);
        }

        private static bool IsOlderContract(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            int currentMajor, currentMinor, currentPatch, major, minor, patch;
            if (!TryVersion(CurrentContract, out currentMajor, out currentMinor, out currentPatch) ||
                !TryVersion(value, out major, out minor, out patch)) return false;
            if (major != currentMajor) return major < currentMajor;
            if (minor != currentMinor) return minor < currentMinor;
            return patch < currentPatch;
        }

        private static bool TryVersion(string value, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            string[] parts = value.Split('.');
            return parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch);
        }

        private static string JoinErrors(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second)) return first;
            return first + " | " + second;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private sealed class ReadResult
        {
            public SaveStatus Status;
            public StreetSaveData Value;
            public string Error;
            public static ReadResult Missing() { return new ReadResult { Status = SaveStatus.Missing }; }
            public static ReadResult Valid(StreetSaveData value) { return new ReadResult { Status = SaveStatus.Success, Value = value }; }
            public static ReadResult Failure(SaveStatus status, string error) { return new ReadResult { Status = status, Error = error }; }
        }
    }
}
