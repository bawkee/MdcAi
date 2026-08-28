import '@testing-library/jest-dom';
import { installFakeWebView } from './testUtils';

// App.js posts { Name: "Ready" } at module load, so the fake must be in place
// before any test file imports App.
installFakeWebView();

// jsdom doesn't implement these scrolling APIs; silence the noise. The scroll
// behaviour itself is covered by the tests that drive 'scroll' events.
window.scrollTo = jest.fn();
Element.prototype.scrollIntoView = jest.fn();