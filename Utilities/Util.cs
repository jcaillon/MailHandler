#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (Util.cs) is part of MailHandler.
// 
// MailHandler is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// MailHandler is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MailHandler. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MimeKit;

namespace MailHandler.Utilities {
    public static class Util {
        /// <summary>
        /// Get a token that cancels after 1 minute
        /// </summary>
        /// <param name="ms">milleseconds to wait before cancel - default 60000</param>
        /// <returns></returns>
        public static CancellationToken GetCancellationToken(int ms = (1000 * 60 * 1)) {
            var token = new CancellationTokenSource(ms);
            return token.Token;
        }

        /// <summary>
        /// Format this to be human readable
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public static string Format(this InternetAddressList list) {
            return list.Mailboxes.Format();
        }

        /// <summary>
        /// Format this to be human readable
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public static string Format(this IEnumerable<MailboxAddress> list) {
            return string.Join("; ", list.Select(address => string.IsNullOrEmpty(address.Name) ? $"{address.Address}" : $"{address.Name} ({address.Address})"));
        }
    }
}