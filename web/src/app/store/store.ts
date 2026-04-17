import { configureStore } from '@reduxjs/toolkit';
import { starterApi } from '../../shared/api/base/starterBaseApi';

export const store = configureStore({
  reducer: {
    [starterApi.reducerPath]: starterApi.reducer,
  },
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware({
      serializableCheck: false,
    }).concat(starterApi.middleware),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;