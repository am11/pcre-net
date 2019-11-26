﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using PCRE.Internal;

namespace PCRE
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "IntroduceOptionalParameters.Global")]
    public partial class PcreRegex
    {
        [Pure]
        public bool IsMatch(string subject)
            => IsMatch(subject, 0);

        [Pure]
        public bool IsMatch(string subject, int startIndex)
            => Match(subject, startIndex).Success;

        [Pure]
        public PcreMatch Match(string subject)
            => Match(subject, 0, PcreMatchOptions.None, null);

        [Pure]
        public PcreMatch Match(string subject, PcreMatchOptions options)
            => Match(subject, 0, options, null);

        [Pure]
        public PcreMatch Match(string subject, int startIndex)
            => Match(subject, startIndex, PcreMatchOptions.None, null);

        [Pure]
        public PcreMatch Match(string subject, int startIndex, PcreMatchOptions options)
            => Match(subject, startIndex, options, null);

        public PcreMatch Match(string subject, Func<PcreCallout, PcreCalloutResult> onCallout)
            => Match(subject, 0, PcreMatchOptions.None, onCallout);

        public PcreMatch Match(string subject, PcreMatchOptions options, Func<PcreCallout, PcreCalloutResult> onCallout)
            => Match(subject, 0, options, onCallout);

        public PcreMatch Match(string subject, int startIndex, Func<PcreCallout, PcreCalloutResult> onCallout)
            => Match(subject, startIndex, PcreMatchOptions.None, onCallout);

        public PcreMatch Match(string subject, int startIndex, PcreMatchOptions options, Func<PcreCallout, PcreCalloutResult> onCallout)
            => Match(subject, PcreMatchSettings.GetSettings(startIndex, options, onCallout));

        public PcreMatch Match(string subject, PcreMatchSettings settings)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (settings.StartIndex < 0 || settings.StartIndex > subject.Length)
                throw new IndexOutOfRangeException("Invalid StartIndex value");

            return InternalRegex.Match(subject, settings, settings.StartIndex, settings.AdditionalOptions.ToPatternOptions());
        }

        [Pure]
        public IEnumerable<PcreMatch> Matches(string subject)
            => Matches(subject, 0, null);

        [Pure]
        public IEnumerable<PcreMatch> Matches(string subject, int startIndex)
            => Matches(subject, startIndex, null);

        [Pure]
        public IEnumerable<PcreMatch> Matches(string subject, int startIndex, Func<PcreCallout, PcreCalloutResult> onCallout)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));

            return Matches(subject, PcreMatchSettings.GetSettings(startIndex, PcreMatchOptions.None, onCallout));
        }

        [Pure]
        public IEnumerable<PcreMatch> Matches(string subject, PcreMatchSettings settings)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (settings.StartIndex < 0 || settings.StartIndex > subject.Length)
                throw new IndexOutOfRangeException("Invalid StartIndex value");

            return MatchesIterator(subject, settings);
        }

        private IEnumerable<PcreMatch> MatchesIterator(string subject, PcreMatchSettings settings)
        {
            var match = InternalRegex.Match(subject, settings, settings.StartIndex, settings.AdditionalOptions.ToPatternOptions());
            if (!match.Success)
                yield break;

            yield return match;

            var baseOptions = settings.AdditionalOptions.ToPatternOptions() | PcreConstants.NO_UTF_CHECK;
            var forceAdvance = false;

            while (true)
            {
                var startIndex = match.GetStartOfNextMatchIndex();
                var forceNonEmptyMatch = match.Length == 0;

                if (forceAdvance)
                {
                    forceAdvance = false;

                    if (startIndex == match.Index)
                    {
                        ++startIndex;
                        forceNonEmptyMatch = false;
                    }
                }

                var options = baseOptions | (forceNonEmptyMatch ? PcreConstants.NOTEMPTY_ATSTART : 0);

                match = InternalRegex.Match(subject, settings, startIndex, options);
                if (!match.Success)
                    yield break;

                if (forceNonEmptyMatch && match.Length == 0 && match.Index == startIndex)
                {
                    // Unexpectedly got an empty match twice at the same position :'(
                    forceAdvance = true;
                    continue;
                }

                yield return match;
            }
        }

        [Pure]
        public static bool IsMatch(string subject, string pattern)
            => IsMatch(subject, pattern, PcreOptions.None, 0);

        [Pure]
        public static bool IsMatch(string subject, string pattern, PcreOptions options)
            => IsMatch(subject, pattern, options, 0);

        [Pure]
        public static bool IsMatch(string subject, string pattern, PcreOptions options, int startIndex)
            => new PcreRegex(pattern, options).IsMatch(subject, startIndex);

        [Pure]
        public static PcreMatch Match(string subject, string pattern)
            => Match(subject, pattern, PcreOptions.None, 0);

        [Pure]
        public static PcreMatch Match(string subject, string pattern, PcreOptions options)
            => Match(subject, pattern, options, 0);

        [Pure]
        public static PcreMatch Match(string subject, string pattern, PcreOptions options, int startIndex)
            => new PcreRegex(pattern, options).Match(subject, startIndex);

        [Pure]
        public static IEnumerable<PcreMatch> Matches(string subject, string pattern)
            => Matches(subject, pattern, PcreOptions.None, 0);

        [Pure]
        public static IEnumerable<PcreMatch> Matches(string subject, string pattern, PcreOptions options)
            => Matches(subject, pattern, options, 0);

        [Pure]
        public static IEnumerable<PcreMatch> Matches(string subject, string pattern, PcreOptions options, int startIndex)
            => new PcreRegex(pattern, options).Matches(subject, startIndex);
    }
}
