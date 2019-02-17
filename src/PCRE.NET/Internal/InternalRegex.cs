﻿using System;
using System.Collections.Generic;
using System.Threading;
using PCRE.Dfa;

namespace PCRE.Internal
{
    internal sealed unsafe class InternalRegex : IDisposable
    {
        private IntPtr _code;
        private Dictionary<int, PcreCalloutInfo> _calloutInfoByPatternPosition;

        public string Pattern { get; }
        public PcreRegexSettings Settings { get; }

        public Dictionary<string, int[]> CaptureNames { get; }
        public int CaptureCount { get; }

        public InternalRegex(string pattern, PcreRegexSettings settings)
        {
            Pattern = pattern;
            Settings = settings.AsReadOnly();

            Native.compile_result result;

            fixed (char* pPattern = pattern)
            {
                var input = new Native.compile_input
                {
                    pattern = pPattern,
                    pattern_length = (uint)pattern.Length
                };

                Settings.FillCompileInput(ref input);

                Native.compile(ref input, out result);
                _code = result.code;

                if (_code == IntPtr.Zero || result.error_code != 0)
                    throw new ArgumentException($"Invalid pattern '{pattern}': {Native.GetErrorMessage(result.error_code)} at offset {result.error_offset}");
            }

            CaptureCount = (int)result.capture_count;
            CaptureNames = new Dictionary<string, int[]>((int)result.name_count, StringComparer.Ordinal);

            var currentItem = result.name_entry_table;

            for (var i = 0; i < result.name_count; ++i)
            {
                var groupIndex = (int)*currentItem;
                var groupName = new string(currentItem + 1);

                if (CaptureNames.TryGetValue(groupName, out var indexes))
                {
                    Array.Resize(ref indexes, indexes.Length + 1);
                    indexes[indexes.Length - 1] = groupIndex;
                }
                else
                {
                    indexes = new[] { groupIndex };
                }

                CaptureNames[groupName] = indexes;

                currentItem += result.name_entry_size;
            }
        }

        ~InternalRegex()
        {
            FreeCode();
        }

        public void Dispose()
        {
            FreeCode();
            GC.SuppressFinalize(this);
        }

        private void FreeCode()
        {
            if (_code != IntPtr.Zero)
            {
                Native.code_free(_code);
                _code = IntPtr.Zero;
            }
        }

        public uint GetInfoUInt32(uint key)
        {
            uint result;
            var errorCode = Native.pattern_info(_code, key, &result);

            if (errorCode != 0)
                throw new InvalidOperationException($"Error in pcre2_pattern_info: {Native.GetErrorMessage(errorCode)}");

            return result;
        }

        public UIntPtr GetInfoNativeInt(uint key)
        {
            UIntPtr result;
            var errorCode = Native.pattern_info(_code, key, &result);

            if (errorCode != 0)
                throw new InvalidOperationException($"Error in pcre2_pattern_info: {Native.GetErrorMessage(errorCode)}");

            return result;
        }

        public PcreMatch Match(string subject, PcreMatchSettings settings)
        {
            var input = new Native.match_input();
            settings.FillMatchInput(ref input);
            return Match(subject, settings, ref input);
        }

        public PcreMatch Match(string subject, PcreMatchSettings settings, int startIndex, uint additionalOptions)
        {
            var input = new Native.match_input();
            settings.FillMatchInput(ref input);
            input.start_index = (uint)startIndex;
            input.additional_options = additionalOptions;
            return Match(subject, settings, ref input);
        }

        private PcreMatch Match(string subject, PcreMatchSettings settings, ref Native.match_input input)
        {
            var oVector = new uint[2 * (CaptureCount + 1)];
            Native.match_result result;
            CalloutInterop.CalloutInteropInfo calloutInterop;

            fixed (char* pSubject = subject)
            fixed (uint* pOVec = &oVector[0])
            {
                input.code = _code;
                input.subject = pSubject;
                input.subject_length = (uint)subject.Length;
                input.output_vector = pOVec;

                CalloutInterop.Prepare(subject, this, ref input, out calloutInterop, settings.Callout);

                Native.match(ref input, out result);
            }

            AfterMatch(result, ref calloutInterop);

            return new PcreMatch(subject, this, ref result, oVector);
        }

        public PcreDfaMatchResult DfaMatch(string subject, PcreDfaMatchSettings settings, int startIndex)
        {
            var input = new Native.dfa_match_input();
            settings.FillMatchInput(ref input);

            var oVector = new uint[2 * Math.Max(1, settings.MaxResults)];
            Native.match_result result;
            CalloutInterop.CalloutInteropInfo calloutInterop;

            fixed (char* pSubject = subject)
            fixed (uint* pOVec = &oVector[0])
            {
                input.code = _code;
                input.subject = pSubject;
                input.subject_length = (uint)subject.Length;
                input.output_vector = pOVec;
                input.start_index = (uint)startIndex;

                CalloutInterop.Prepare(subject, this, ref input, out calloutInterop, settings.Callout);

                Native.dfa_match(ref input, out result);
            }

            AfterMatch(result, ref calloutInterop);

            return new PcreDfaMatchResult(subject, ref result, oVector);
        }

        private static void AfterMatch(Native.match_result result, ref CalloutInterop.CalloutInteropInfo calloutInterop)
        {
            switch (result.result_code)
            {
                case PcreConstants.ERROR_NOMATCH:
                case PcreConstants.ERROR_PARTIAL:
                    break;

                case PcreConstants.ERROR_CALLOUT:
                    throw new PcreCalloutException("An exception was thrown by the callout: " + calloutInterop.Exception?.Message, calloutInterop.Exception);

                default:
                    if (result.result_code < 0)
                        throw new PcreMatchException(Native.GetErrorMessage(result.result_code));

                    break;
            }
        }

        public IReadOnlyList<PcreCalloutInfo> GetCallouts()
        {
            var calloutCount = Native.get_callout_count(_code);
            if (calloutCount == 0)
                return Array.Empty<PcreCalloutInfo>();

            var data = new Native.pcre2_callout_enumerate_block[calloutCount];

            fixed (Native.pcre2_callout_enumerate_block* pData = &data[0])
            {
                Native.get_callouts(_code, pData);
            }

            var result = new List<PcreCalloutInfo>((int)calloutCount);

            for (var i = 0; i < data.Length; ++i)
                result.Add(new PcreCalloutInfo(ref data[i]));

            return result.AsReadOnly();
        }

        public PcreCalloutInfo GetCalloutInfoByPatternPosition(int patternPosition)
        {
            if (_calloutInfoByPatternPosition == null)
            {
                var dict = new Dictionary<int, PcreCalloutInfo>();

                foreach (var info in GetCallouts())
                    dict.Add(info.PatternPosition, info);

                Thread.MemoryBarrier();
                _calloutInfoByPatternPosition = dict;
            }

            return _calloutInfoByPatternPosition.TryGetValue(patternPosition, out var result) ? result : null;
        }
    }
}
