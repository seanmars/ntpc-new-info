import { createRouter, createWebHistory } from 'vue-router';
import RescueMapView from '../views/RescueMapView.vue';

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'rescue-map',
      component: RescueMapView,
    },
  ],
});

export default router;
