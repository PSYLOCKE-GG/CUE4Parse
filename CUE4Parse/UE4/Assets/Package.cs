using System;
using System.Collections.Generic;
using System.IO;
using CUE4Parse.FileProvider;
using CUE4Parse.GameTypes.ACE7.Encryption;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Assets.Utils;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using Serilog;

namespace CUE4Parse.UE4.Assets
{
    [SkipObjectRegistration]
    public sealed class Package : AbstractUePackage
    {
        public override FPackageFileSummary Summary { get; }
        public override FNameEntrySerialized[] NameMap { get; }
        public override int ImportMapLength => ImportMap.Length;
        public override int ExportMapLength => ExportMap.Length;

        public FObjectImport[] ImportMap { get; }
        public FObjectExport[] ExportMap { get; }
        public FPackageIndex[][]? DependsMap { get; }
        public FPackageIndex[]? PreloadDependencies { get; }
        public FObjectDataResource[]? DataResourceMap { get; }
        public FSoftObjectPath[] SoftObjectPaths { get; }
        public List<byte[]>? EditorThumbnails { get; }
        public FPackageTrailer? Trailer { get; }

        public Package(FArchive uasset, FArchive? uexp, FArchive? ubulk = null, FArchive? uptnl = null, IFileProvider? provider = null, bool useLazySerialization = true)
            : this(
                uasset,
                uexp,
                ubulk != null ? _ => ubulk : null,
                uptnl != null ? _ => uptnl : null,
                provider,
                useLazySerialization)
        { }

        public Package(string name, byte[] uasset, byte[]? uexp, byte[]? ubulk = null, byte[]? uptnl = null, IFileProvider? provider = null, bool useLazySerialization = true)
            : this(
                new FByteArchive($"{name}.uasset", uasset),
                uexp != null ? new FByteArchive($"{name}.uexp", uexp) : null,
                ubulk != null ? new FByteArchive($"{name}.ubulk", ubulk) : null,
                uptnl != null ? new FByteArchive($"{name}.uptnl", uptnl) : null,
                provider,
                useLazySerialization)
        { }

        public Package(
            FArchive uasset,
            FArchive? uexp,
            Func<FByteBulkDataHeader?, FArchive?>? ubulk = null,
            Func<FByteBulkDataHeader?, FArchive?>? uptnl = null,
            IFileProvider? provider = null,
            bool useLazySerialization = true)
            : base(uasset.Name.SubstringBeforeLast('.'), provider)
        {
            // We clone the version container because it can be modified with package specific versions when reading the summary
            uasset.Versions = (VersionContainer) uasset.Versions.Clone();

            FAssetArchive uassetAr;
            ACE7XORKey? xorKey = null;
            ACE7Decrypt? decryptor = null;
            if (uasset.Game == EGame.GAME_AceCombat7)
            {
                decryptor = new ACE7Decrypt();
                uassetAr = new FAssetArchive(decryptor.DecryptUassetArchive(uasset, out xorKey), this);
            }
            else uassetAr = new FAssetArchive(uasset, this);

            Summary = new FPackageFileSummary(uassetAr);

            uassetAr.SeekAbsolute(Summary.NameOffset, SeekOrigin.Begin);
            NameMap = new FNameEntrySerialized[Summary.NameCount];
            uassetAr.ReadArray(NameMap, () => new FNameEntrySerialized(uassetAr));

            uassetAr.SeekAbsolute(Summary.ImportOffset, SeekOrigin.Begin);
            ImportMap = new FObjectImport[Summary.ImportCount];
            uassetAr.ReadArray(ImportMap, () => new FObjectImport(uassetAr));

            uassetAr.SeekAbsolute(Summary.ExportOffset, SeekOrigin.Begin);
            ExportMap = new FObjectExport[Summary.ExportCount]; // we need this to get its final size in some case
            uassetAr.ReadArray(ExportMap, () => new FObjectExport(uassetAr));

            if (Summary.ThumbnailTableOffset > 0)
            {
                uassetAr.SeekAbsolute(Summary.ThumbnailTableOffset, SeekOrigin.Begin);
                var count = uassetAr.Read<int>();

                // Pre-allocate with known capacity to avoid resizing
                EditorThumbnails = new List<byte[]>(count);
                var thumbnailOffsets = new List<int>(count);

                for (int i = 0; i < count; i++)
                {
                    uassetAr.SkipFString(); // objectShortClassName
                    uassetAr.SkipFString(); // objectPathWithoutPackageName
                    var thumbnailOffset = uassetAr.Read<int>();
                    thumbnailOffsets.Add(thumbnailOffset);
                }

                foreach (var offset in thumbnailOffsets)
                {
                    uassetAr.SeekAbsolute(offset + 8, SeekOrigin.Begin);
                    var totalBytes = uassetAr.Read<int>();
                    if (totalBytes == 0) continue;
                    var rawImage = uassetAr.ReadBytes(totalBytes);
                    EditorThumbnails.Add(rawImage);
                }
            }

            _ = useLazySerialization; // parameter retained for backwards compat; ExportInfo provides the phased split unconditionally.

            if (Summary is { DependsOffset: > 0, ExportCount: > 0 })
            {
                uassetAr.SeekAbsolute(Summary.DependsOffset, SeekOrigin.Begin);
                DependsMap = uassetAr.ReadArray(Summary.ExportCount, () => uassetAr.ReadArray(() => new FPackageIndex(uassetAr)));
            }

            if (Summary is { PreloadDependencyCount: > 0, PreloadDependencyOffset: > 0 })
            {
                uassetAr.SeekAbsolute(Summary.PreloadDependencyOffset, SeekOrigin.Begin);
                PreloadDependencies = uassetAr.ReadArray(Summary.PreloadDependencyCount, () => new FPackageIndex(uassetAr));
            }

            if (Summary is { SoftObjectPathsCount: > 0, SoftObjectPathsOffset: > 0 })
            {
                uassetAr.SeekAbsolute(Summary.SoftObjectPathsOffset, SeekOrigin.Begin);
                SoftObjectPaths = uassetAr.ReadArray(Summary.SoftObjectPathsCount, () => new FSoftObjectPath(uassetAr));
            }
            else
            {
                SoftObjectPaths = [];
            }

            // if (Summary.SoftPackageReferencesCount > 0)
            // {
            //     uassetAr.SeekAbsolute(Summary.SoftPackageReferencesOffset, SeekOrigin.Begin);
            //     SoftPackageReferences = uassetAr.ReadArray(Summary.SoftPackageReferencesCount, () => FPackageId.FromName(uassetAr.ReadFName()));
            // }

            if (Summary.DataResourceOffset > 0)
            {
                uassetAr.SeekAbsolute(Summary.DataResourceOffset, SeekOrigin.Begin);
                var dataResourceVersion = (EObjectDataResourceVersion) uassetAr.Read<uint>();
                if (dataResourceVersion is > EObjectDataResourceVersion.Invalid and <= EObjectDataResourceVersion.Latest)
                {
                    DataResourceMap = uassetAr.ReadArray(() => new FObjectDataResource(uassetAr, dataResourceVersion));
                }
            }

            if (!Summary.PackageFlags.HasFlag(EPackageFlags.PKG_Cooked) && Summary.PayloadTocOffset > 0)
            {
                uassetAr.SeekAbsolute(Summary.PayloadTocOffset, SeekOrigin.Begin);
                Trailer = new FPackageTrailer(uassetAr);
            }

            if (!CanDeserialize) return;

            FAssetArchive uexpAr;
            if (uexp != null)
            {
                if (uasset.Game == EGame.GAME_AceCombat7 && decryptor != null && xorKey != null)
                {
                    uexpAr = new FAssetArchive(decryptor.DecryptUexpArchive(uexp, xorKey), this, (int) uassetAr.Length);
                } else uexpAr = new FAssetArchive(uexp, this, (int) uassetAr.Length);
            }
            else uexpAr = uassetAr;

            if (ubulk != null)
            {
                //var offset = (int) (Summary.TotalHeaderSize + ExportMap.Sum(export => export.SerialSize));
                var offset = Summary.BulkDataStartOffset;
                uexpAr.AddPayload(PayloadType.UBULK, offset, ubulk);
            }

            if (uptnl != null)
            {
                var offset = Summary.BulkDataStartOffset;
                uexpAr.AddPayload(PayloadType.UPTNL, offset, uptnl);
            }

            var exports = new ExportInfo[ExportMap.Length];
            for (var i = 0; i < ExportMap.Length; i++)
            {
                var export = ExportMap[i];
                var idx = i;
                var className = ResolveCheapClassName(export.ClassIndex);

                UObject CreateSparse()
                {
                    var obj = ConstructObject(ResolvePackageIndex(export.ClassIndex), this, (EObjectFlags) export.ObjectFlags);
                    obj.Name = export.ObjectName.Text;
                    obj.Outer = ResolvePackageIndex(export.OuterIndex) as ResolvedExportObject;
                    obj.Outer ??= new ResolvedPackageObject(this);
                    obj.Super = ResolvePackageIndex(export.SuperIndex) as ResolvedExportObject;
                    obj.Template = ResolvePackageIndex(export.TemplateIndex) as ResolvedExportObject;
                    obj.Flags |= (EObjectFlags) export.ObjectFlags;
                    return obj;
                }

                void Deserialize(UObject obj)
                {
                    var Ar = (FAssetArchive) uexpAr.Clone();
                    Ar.SeekAbsolute(export.SerialOffset, SeekOrigin.Begin);
                    DeserializeObject(obj, Ar, export.SerialSize);
                    obj.Flags |= EObjectFlags.RF_LoadCompleted;
                    obj.PostLoad();
                }

                exports[idx] = new ExportInfo(this, idx, export.ObjectName.Text, className, CreateSparse, Deserialize);
            }

            // Publish Exports before priming sparse so same-package ClassIndex refs
            // resolve through ResolvedExportObject.ExportInfo → Exports[i] → EnsureSparse(),
            // which ExportInfo's _sparseConstructing guard handles re-entrantly.
            Exports = exports;

            foreach (var info in exports)
            {
                info.EnsureSparse();
            }

            IsFullyLoaded = true;
        }

        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal)
        {
            for (var i = 0; i < ExportMap.Length; i++)
            {
                if (ExportMap[i].ObjectName.Text.Equals(name, comparisonType))
                {
                    return i;
                }
            }

            return -1;
        }

        private string ResolveCheapClassName(FPackageIndex classIndex)
        {
            if (classIndex.IsNull) return string.Empty;
            if (classIndex.IsImport)
            {
                var importIdx = -classIndex.Index - 1;
                if (importIdx >= 0 && importIdx < ImportMap.Length)
                    return ImportMap[importIdx].ObjectName.Text;
            }
            else if (classIndex.IsExport)
            {
                var exportIdx = classIndex.Index - 1;
                if (exportIdx >= 0 && exportIdx < ExportMap.Length)
                    return ExportMap[exportIdx].ObjectName.Text;
            }
            return string.Empty;
        }

        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index)
        {
            if (index == null || index.IsNull)
                return null;
            if (index.IsImport && -index.Index - 1 < ImportMap.Length)
                return ResolveImport(index);
            if (index.IsExport && index.Index - 1 < ExportMap.Length)
                return new ResolvedExportObject(index.Index - 1, this);
            return null;
        }

        private ResolvedObject? ResolveImport(FPackageIndex importIndex)
        {
            var import = ImportMap[-importIndex.Index - 1];
            var outerMostIndex = importIndex;
            FObjectImport outerMostImport;
            while (true)
            {
                // special case when the outermost import is an export in this package
                if (outerMostIndex.IsExport)
                    return new ResolvedImportObject(import, this);

                outerMostImport = ImportMap[-outerMostIndex.Index - 1];
                if (outerMostImport.OuterIndex.IsNull)
                    break;
                outerMostIndex = outerMostImport.OuterIndex;
            }

            outerMostImport = ImportMap[-outerMostIndex.Index - 1];
            // We don't support loading script packages, so just return a fallback
            if (outerMostImport.ObjectName.Text.StartsWith("/Script/"))
            {
                return new ResolvedImportObject(import, this);
            }

            if (Provider == null)
                return null;
            Package? importPackage = null;
            if (Provider.TryLoadPackage(outerMostImport.ObjectName.Text, out var package))
            {
                if (package is IoPackage ioPackage)
                {
                    for (int i = 0; i < ioPackage.ExportMap.Length; i++)
                    {
                        FExportMapEntry export = ioPackage.ExportMap[i];
                        if (ioPackage.CreateFNameFromMappedName(export.ObjectName).Text == import.ObjectName.Text)
                        {
                            return ioPackage.ResolvePackageIndex(new FPackageIndex(ioPackage, i + 1));
                        }
                    }
#if DEBUG
                    Log.Fatal("Missing import of ({0}): {1} in {2} was not found, but the package exists.", Name, import.ObjectName, ioPackage.GetFullName());
#endif
                    return new ResolvedImportObject(import, this);
                }
                else
                    importPackage = package as Package;
            }
            if (importPackage == null)
            {
#if DEBUG
                Log.Error("Missing native package ({0}) for import of {1} in {2}.", outerMostImport.ObjectName, import.ObjectName, Name);
#endif
                return new ResolvedImportObject(import, this);
            }

            string? outer = null;
            if (outerMostIndex != import.OuterIndex && import.OuterIndex.IsImport)
            {
                var outerImport = ImportMap[-import.OuterIndex.Index - 1];
                outer = ResolveImport(import.OuterIndex)?.GetPathName();
                if (outer == null)
                {
#if DEBUG
                    Log.Fatal("Missing outer for import of ({0}): {1} in {2} was not found, but the package exists.", Name, outerImport.ObjectName, importPackage.GetFullName());
#endif
                    return new ResolvedImportObject(import, this);
                }
            }

            for (var i = 0; i < importPackage.ExportMap.Length; i++)
            {
                var export = importPackage.ExportMap[i];
                if (export.ObjectName.Text != import.ObjectName.Text)
                    continue;
                var thisOuter = importPackage.ResolvePackageIndex(export.OuterIndex);
                if (thisOuter?.GetPathName() == outer)
                    return new ResolvedExportObject(i, importPackage);
            }

#if DEBUG
            Log.Fatal("Missing import of ({0}): {1} in {2} was not found, but the package exists.", Name, import.ObjectName, importPackage.GetFullName());
#endif
            return new ResolvedImportObject(import, this);
        }

        private class ResolvedExportObject : ResolvedObject
        {
            private readonly FObjectExport _export;

            public ResolvedExportObject(int exportIndex, Package package) : base(package, exportIndex)
            {
                _export = package.ExportMap[exportIndex];
            }

            public override FName Name => _export?.ObjectName ?? "None";
            public override ResolvedObject Outer => Package.ResolvePackageIndex(_export.OuterIndex) ?? new ResolvedPackageObject(Package);
            public override ResolvedObject? Class => Package.ResolvePackageIndex(_export.ClassIndex);
            public override ResolvedObject? Super => Package.ResolvePackageIndex(_export.SuperIndex);
        }

        /** Fallback if we cannot resolve the export in another package */
        private class ResolvedImportObject : ResolvedObject
        {
            private readonly FObjectImport _import;

            public ResolvedImportObject(FObjectImport import, Package package) : base(package)
            {
                _import = import;
            }

            public override FName Name => _import.ObjectName;
            public override ResolvedObject? Outer => Package.ResolvePackageIndex(_import.OuterIndex);
            public override ResolvedObject Class => new ResolvedLoadedObject(new UScriptClass(_import.ClassName.Text));
            public override UObject? GetDirectObject() => _import.ClassName.Text switch
            {
                "Class" => new UScriptClass(Name.Text),
                "SharpClass" => new USharpClass(Name.Text),
                "PythonClass" => new UPythonClass(Name.Text),
                "ASClass" => new UASClass(Name.Text),
                "ScriptStruct" => new UScriptClass(Name.Text),
                _ => null
            };
        }
    }
}
