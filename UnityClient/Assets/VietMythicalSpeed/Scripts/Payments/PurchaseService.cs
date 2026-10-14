using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Auth;
using VMS.Backend;
using VMS.Core;

namespace VMS.Payments
{
    /// <summary>A way to pay real money. Every purchase is verified and granted by the server, never by the client.</summary>
    public interface IPaymentStore
    {
        string Name { get; }
        /// <summary>The store's localised price ("₫25.000", "$0.99"), or null to show the catalog's USD fallback.</summary>
        string PriceLabel(ProductDef product);
        Task<PurchaseResponse> BuyAsync(ProductDef product);
    }

    /// <summary>
    /// Picks the payment store for this build: Google Play (Unity IAP) on Android, the Steam wallet on PC with
    /// Steamworks, or – in the editor and development builds when the backend allows it – a dev store that
    /// grants products for testing. Release builds without a store simply can't buy gold.
    /// </summary>
    public sealed class PurchaseService
    {
        readonly IBackend _backend;
        IPaymentStore _store;

#pragma warning disable 0067 // raised only by the Google Play store (Android builds)
        /// <summary>A purchase finished outside a Buy call (e.g. the app restarted mid-purchase) and was delivered.</summary>
        public event Action<PurchaseResponse> DeliveredLater;
#pragma warning restore 0067

        public PurchaseService(IBackend backend)
        {
            _backend = backend;
        }

        public IPaymentStore Store => _store;
        public bool Available => _store != null;

        /// <summary>Called once the shop catalog and the server's supported stores are known.</summary>
        public void Configure(ShopCatalog catalog, StoresInfo stores)
        {
            if (_store != null) return;
#if VMS_UNITY_IAP && UNITY_ANDROID && !UNITY_EDITOR
            if (_backend.IsOnline && stores != null && stores.google)
            {
                var iap = new UnityIapStore(_backend);
                iap.DeliveredLater += r => DeliveredLater?.Invoke(r);
                iap.Init(catalog);
                _store = iap;
                return;
            }
#endif
            if (_backend.IsOnline && stores != null && stores.steam && SteamworksBridge.IsAvailable)
            {
                _store = new SteamStore(_backend);
                return;
            }
            if ((Application.isEditor || Debug.isDebugBuild) && stores != null && stores.fake)
                _store = new DevStore(_backend);
        }

        public string PriceLabel(ProductDef product)
        {
            string label = _store != null ? _store.PriceLabel(product) : null;
            return !string.IsNullOrEmpty(label) ? label : "$" + product.priceUsd.ToString("0.00", CultureInfo.InvariantCulture);
        }

        public async Task<PurchaseResponse> BuyAsync(ProductDef product)
        {
            if (_store == null) throw new BackendException("store_unavailable", "Purchases aren't available in this version.");
            // One-time packs: ask before the store charges anything.
            if (product.limit > 0 && !await _backend.CanBuyProductAsync(product.id))
                throw new BackendException("limit_reached", "Already bought.");
            return await _store.BuyAsync(product);
        }
    }

    /// <summary>Development: the server (PAYMENTS_FAKE=1) or the offline backend grants products without payment.</summary>
    public sealed class DevStore : IPaymentStore
    {
        readonly IBackend _backend;

        public DevStore(IBackend backend)
        {
            _backend = backend;
        }

        public string Name => "dev";

        public string PriceLabel(ProductDef product) => "$" + product.priceUsd.ToString("0.00", CultureInfo.InvariantCulture) + " (test)";

        public Task<PurchaseResponse> BuyAsync(ProductDef product) => _backend.FakePurchaseAsync(product.id);
    }

    /// <summary>
    /// Steam wallet: the server opens the order (InitTxn), Steam's overlay asks the player, and the server
    /// charges it (FinalizeTxn) once the client reports the overlay's answer.
    /// </summary>
    public sealed class SteamStore : IPaymentStore
    {
        readonly IBackend _backend;

        public SteamStore(IBackend backend)
        {
            _backend = backend;
        }

        public string Name => "steam";

        public string PriceLabel(ProductDef product) => null;

        public async Task<PurchaseResponse> BuyAsync(ProductDef product)
        {
            string orderId = await _backend.StartSteamPurchaseAsync(product.id, Loc.Language);
            var answer = new TaskCompletionSource<bool>();
            Action<ulong, bool> handler = (id, authorized) =>
            {
                if (id.ToString(CultureInfo.InvariantCulture) == orderId) answer.TrySetResult(authorized);
            };
            SteamworksBridge.MicroTxnAuthorization += handler;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                using (timeout.Token.Register(() => answer.TrySetResult(false)))
                {
                    bool authorized = await answer.Task;
                    return await _backend.FinishSteamPurchaseAsync(orderId, authorized);
                }
            }
            finally
            {
                SteamworksBridge.MicroTxnAuthorization -= handler;
            }
        }
    }

#if VMS_UNITY_IAP
    /// <summary>
    /// Google Play Billing through Unity IAP 5. Orders stay pending until the server has verified and granted
    /// them; only then are they confirmed (consumed). Orders interrupted by a crash or a lost connection are
    /// re-delivered by Google on the next launch and finished then.
    /// </summary>
    public sealed class UnityIapStore : IPaymentStore
    {
        readonly IBackend _backend;
        UnityEngine.Purchasing.StoreController _controller;
        TaskCompletionSource<PurchaseResponse> _waiting;
        string _waitingProduct;

        public event Action<PurchaseResponse> DeliveredLater;

        public UnityIapStore(IBackend backend)
        {
            _backend = backend;
        }

        public string Name => "google";

        public async void Init(ShopCatalog catalog)
        {
            try
            {
                _controller = UnityEngine.Purchasing.UnityIAPServices.StoreController();
                _controller.OnPurchasePending += OnPurchasePending;
                _controller.OnPurchaseFailed += OnPurchaseFailed;
                _controller.OnPurchaseDeferred += OnPurchaseDeferred;
                _controller.OnProductsFetched += _ => _controller.FetchPurchases();
                _controller.ProcessPendingOrdersOnPurchasesFetched(true);
                await _controller.Connect();
                var definitions = new List<UnityEngine.Purchasing.ProductDefinition>();
                foreach (var p in catalog.products)
                    definitions.Add(new UnityEngine.Purchasing.ProductDefinition(p.id, UnityEngine.Purchasing.ProductType.Consumable));
                _controller.FetchProducts(definitions);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IAP] Google Play Billing unavailable: " + e.Message);
            }
        }

        public string PriceLabel(ProductDef product)
        {
            var p = _controller != null ? _controller.GetProductById(product.id) : null;
            return p != null && p.metadata != null ? p.metadata.localizedPriceString : null;
        }

        public Task<PurchaseResponse> BuyAsync(ProductDef product)
        {
            if (_controller == null) throw new BackendException("store_unavailable", "Google Play Billing isn't ready.");
            if (_waiting != null) throw new BackendException("purchase_in_progress", "Another purchase is in progress.");
            _waiting = new TaskCompletionSource<PurchaseResponse>();
            _waitingProduct = product.id;
            _controller.PurchaseProduct(product.id);
            return _waiting.Task;
        }

        static string ProductOf(UnityEngine.Purchasing.Order order)
        {
            var items = order.CartOrdered != null ? order.CartOrdered.Items() : null;
            return items != null && items.Count > 0 && items[0].Product != null ? items[0].Product.definition.id : null;
        }

        void Finish(string productId, PurchaseResponse response, Exception error)
        {
            if (_waiting != null && productId == _waitingProduct)
            {
                var tcs = _waiting;
                _waiting = null;
                if (error != null) tcs.TrySetException(error);
                else tcs.TrySetResult(response);
            }
            else if (response != null)
            {
                DeliveredLater?.Invoke(response);
            }
        }

        async void OnPurchasePending(UnityEngine.Purchasing.PendingOrder order)
        {
            string productId = ProductOf(order);
            var google = order.Info != null ? order.Info.Google : null;
            if (productId == null || google == null || string.IsNullOrEmpty(google.PurchaseToken))
            {
                Finish(productId, null, new BackendException("purchase_invalid", "Google Play sent an incomplete order."));
                return;
            }
            try
            {
                var response = await _backend.VerifyGooglePurchaseAsync(productId, google.PurchaseToken);
                // Granted (or already granted before): now let Google consume it.
                _controller.ConfirmPurchase(order);
                Finish(productId, response, null);
            }
            catch (BackendException e)
            {
                // Network/server trouble: leave the order pending; Google re-delivers it on the next launch.
                // A forged or refunded order is consumed so it stops coming back.
                if (e.Code == "purchase_invalid" || e.Code == "purchase_refunded" || e.Code == "purchase_owned_elsewhere") _controller.ConfirmPurchase(order);
                Finish(productId, null, e);
            }
        }

        void OnPurchaseFailed(UnityEngine.Purchasing.FailedOrder order)
        {
            string code = order.FailureReason == UnityEngine.Purchasing.PurchaseFailureReason.UserCancelled ? "cancelled" : "purchase_failed";
            Finish(ProductOf(order), null, new BackendException(code, order.Details));
        }

        void OnPurchaseDeferred(UnityEngine.Purchasing.DeferredOrder order)
        {
            Finish(ProductOf(order), null, new BackendException("purchase_pending", "The payment is pending."));
        }
    }
#endif
}
