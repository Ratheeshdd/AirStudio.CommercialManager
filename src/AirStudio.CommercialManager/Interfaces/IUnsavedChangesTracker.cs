using System.Threading.Tasks;

namespace AirStudio.CommercialManager.Interfaces
{
    /// <summary>
    /// Interface for controls that track unsaved changes
    /// </summary>
    public interface IUnsavedChangesTracker
    {
        /// <summary>
        /// Gets whether the control has unsaved changes
        /// </summary>
        bool HasUnsavedChanges { get; }

        /// <summary>
        /// Gets a description of what has unsaved changes (for display in dialogs)
        /// </summary>
        string UnsavedChangesDescription { get; }

        /// <summary>
        /// Attempt to save current changes
        /// </summary>
        /// <returns>True if save succeeded, false if cancelled or failed</returns>
        Task<bool> SaveChangesAsync();

        /// <summary>
        /// Discard unsaved changes
        /// </summary>
        void DiscardChanges();
    }
}
