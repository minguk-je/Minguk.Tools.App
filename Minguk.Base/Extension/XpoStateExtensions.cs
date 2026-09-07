using DevExpress.Xpo;

using Minguk.Base.Enums;

namespace Minguk.Base.Extension
{
    public static class XpoStateExtensions
    {
        public static XpoDataStates GetDataState(this PersistentBase? obj)
        {
            if (obj == null)
                return XpoDataStates.Unchanged;

            if (obj.IsDeleted)
                return XpoDataStates.Deleted;

            Session s = obj.Session;
            if (s.IsNewObject(obj))
                return XpoDataStates.Added;

            if (s.IsObjectToSave(obj))
                return XpoDataStates.Modified;

            return XpoDataStates.Unchanged;
        }
    }
}
