namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Manages the definition of circles and their access 
    /// </summary>
    public interface ICircleDefinitionService
    {
        /// <summary>
        /// Returns the root system circle that holds the most basic access for its members
        /// </summary>
        /// <returns></returns>
        Circle GetRootCircle();
        
    }
}