using System;

namespace AirStudio.CommercialManager.Core.Models
{
    /// <summary>
    /// Represents an agency from the agency table
    /// </summary>
    public class Agency
    {
        /// <summary>
        /// Agency code (AUTO_INCREMENT primary key)
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// Agency name (mandatory)
        /// </summary>
        public string AgencyName { get; set; }

        /// <summary>
        /// Agency address (optional)
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// PIN code (optional)
        /// </summary>
        public string PIN { get; set; }

        /// <summary>
        /// Phone number (optional)
        /// </summary>
        public string Phone { get; set; }

        /// <summary>
        /// Email address (optional)
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Display string for ComboBox
        /// </summary>
        public string DisplayName => AgencyName;

        /// <summary>
        /// Search-friendly string (for typeahead)
        /// </summary>
        public string SearchText => AgencyName?.ToLowerInvariant() ?? string.Empty;

        /// <summary>
        /// Validate the agency data
        /// </summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(AgencyName);

        /// <summary>
        /// Create a copy of this agency
        /// </summary>
        public Agency Clone()
        {
            return new Agency
            {
                Code = Code,
                AgencyName = AgencyName,
                Address = Address,
                PIN = PIN,
                Phone = Phone,
                Email = Email
            };
        }

        public override string ToString() => DisplayName;
    }
}
