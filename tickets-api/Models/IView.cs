namespace TicketsApi.Models;

public interface IView<TView, TModel>
{
    public static abstract TView ToGetView(TModel model);
    public static abstract List<TView> ToListView(IEnumerable<TModel> items);
}