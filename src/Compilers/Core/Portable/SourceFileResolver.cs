﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Resolves references to source files specified in source code.
    /// </summary>
    public class SourceFileResolver : SourceReferenceResolver, IEquatable<SourceFileResolver>
    {
        public static SourceFileResolver Default { get; } = new SourceFileResolver(ImmutableArray<string>.Empty, baseDirectory: null);

        private readonly string _baseDirectory;
        private readonly ImmutableArray<string> _searchPaths;
        private readonly ImmutableArray<KeyValuePair<string, string>> _pathMap;

        public SourceFileResolver(IEnumerable<string> searchPaths, string baseDirectory)
            : this(searchPaths.AsImmutableOrNull(), baseDirectory)
        {
        }

        public SourceFileResolver(ImmutableArray<string> searchPaths, string baseDirectory)
            : this(searchPaths, baseDirectory, ImmutableArray<KeyValuePair<string, string>>.Empty)
        {
        }

        public SourceFileResolver(
            ImmutableArray<string> searchPaths,
            string baseDirectory,
            ImmutableArray<KeyValuePair<string, string>> pathMap)
        {
            if (searchPaths.IsDefault)
            {
                throw new ArgumentNullException(nameof(searchPaths));
            }

            if (baseDirectory != null && PathUtilities.GetPathKind(baseDirectory) != PathKind.Absolute)
            {
                throw new ArgumentException(CodeAnalysisResources.AbsolutePathExpected, nameof(baseDirectory));
            }

            _baseDirectory = baseDirectory;
            _searchPaths = searchPaths;

            // The previous public API required paths to not end with a path separator.
            // This broke handling of root paths (e.g. "/" cannot be represented), so
            // the new requirement is for paths to always end with a path separator.
            // However, because this is a public API, both conventions must be allowed,
            // so normalize the paths here (instead of enforcing end-with-sep).
            if (!pathMap.IsDefaultOrEmpty)
            {
                var pathMapBuilder = ArrayBuilder<KeyValuePair<string, string>>.GetInstance(pathMap.Length);

                foreach (var kv in pathMap)
                {
                    var key = kv.Key;
                    if (key == null || key.Length == 0)
                    {
                        throw new ArgumentException(CodeAnalysisResources.EmptyKeyInPathMap, nameof(pathMap));
                    }

                    var value = kv.Value;
                    if (value == null)
                    {
                        throw new ArgumentException(CodeAnalysisResources.NullValueInPathMap, nameof(pathMap));
                    }

                    var normalizedKey = PathUtilities.EnsureTrailingSeparator(key);
                    var normalizedValue = PathUtilities.EnsureTrailingSeparator(value);

                    pathMapBuilder.Add(new KeyValuePair<string, string>(normalizedKey, normalizedValue));
                }

                _pathMap = pathMapBuilder.ToImmutableAndFree();
            }
            else
            {
                _pathMap = ImmutableArray<KeyValuePair<string, string>>.Empty;
            }
        }

        public string BaseDirectory => _baseDirectory;

        public ImmutableArray<string> SearchPaths => _searchPaths;

        public ImmutableArray<KeyValuePair<string, string>> PathMap => _pathMap;

        public override string NormalizePath(string path, string baseFilePath)
        {
            string normalizedPath = FileUtilities.NormalizeRelativePath(path, baseFilePath, _baseDirectory);
            return (normalizedPath == null || _pathMap.IsDefaultOrEmpty) ? normalizedPath : PathUtilities.NormalizePathPrefix(normalizedPath, _pathMap);
        }

        public override string ResolveReference(string path, string baseFilePath)
        {
            string resolvedPath = FileUtilities.ResolveRelativePath(path, baseFilePath, _baseDirectory, _searchPaths, FileExists);
            if (resolvedPath == null)
            {
                return null;
            }

            return FileUtilities.TryNormalizeAbsolutePath(resolvedPath);
        }

        public override Stream OpenRead(string resolvedPath)
        {
            CompilerPathUtilities.RequireAbsolutePath(resolvedPath, nameof(resolvedPath));
            return FileUtilities.OpenRead(resolvedPath);
        }

        protected virtual bool FileExists(string resolvedPath)
        {
            return File.Exists(resolvedPath);
        }

        public override bool Equals(object obj)
        {
            // Explicitly check that we're not comparing against a derived type
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return Equals((SourceFileResolver)obj);
        }

        public bool Equals(SourceFileResolver other)
        {
            return
                string.Equals(_baseDirectory, other._baseDirectory, StringComparison.Ordinal) &&
                _searchPaths.SequenceEqual(other._searchPaths, StringComparer.Ordinal) &&
                _pathMap.SequenceEqual(other._pathMap);
        }

        public override int GetHashCode()
        {
            return Hash.Combine(_baseDirectory != null ? StringComparer.Ordinal.GetHashCode(_baseDirectory) : 0,
                   Hash.Combine(Hash.CombineValues(_searchPaths, StringComparer.Ordinal),
                   Hash.CombineValues(_pathMap)));
        }
    }
}
