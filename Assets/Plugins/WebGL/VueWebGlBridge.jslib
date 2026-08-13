mergeInto(LibraryManager.library, {
  VueWebGlPostMessage: function (messagePointer) {
    var message = UTF8ToString(messagePointer);
    setTimeout(function () {
      if (typeof window.receiveUnityMessage === 'function') {
        window.receiveUnityMessage(message);
      }
    }, 0);
  },
});
