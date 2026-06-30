
using SqlNado;

namespace BV6Tools.Services.Database.Extensions
{
    public static class SQLiteDatabaseExtensions
    {
        public static T? LoadByKeys<T>(this SQLiteDatabase db, params object[] keys)
        {
            return db.LoadByPrimaryKey<T>(keys);
        }
    }
}
