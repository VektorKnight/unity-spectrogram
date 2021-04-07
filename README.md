# unity-spectrogram
A basic spectrogram built in Unity with a focus on overall performance.  
Uses a compute shader to generate the base texture and then a very simple gradient lookup shader to color the result.  

This was mostly a weekend project to explore the concept and I do not currently plan to maintain this project beyond bugfixes and performance improvements.  
The important sections of code have been annotated for easy reference and should make it fairly easy to port or reimplement in your own project.  

The UI was hastily written for demonstration purposes only and should probably not be used beyond basic reference.

## Usage
Clone the repository and open up the project in your desired Unity version.  
The project itself was built in **2020.2** but should work in anything recent.  
Open up the **Spectrogram** scene in **Assets/Spectrogram/** and enter play mode to immediately test it with the included audio clip.  
The **Spectrogram** object in the hierarchy has all the configuration values.

## Controls
- Zoom: Scroll
- Pan: Left Click + Drag
- Reset View: Home
- Play/Pause: Spacebar
- Frequency Axis: Ctrl + Scroll
- Time Axis: Shift + Scroll

## Performance
Performance is generally quite good on any system supporting compute shaders.  
The **Extreme** quality setting takes around 0.7ms on my system with an i7-8700K and AMD RX 6800.  
I have not tested this on any mobile platforms as of yet.

## Credits
The included audio clip for testing is from Nosoapradio_us: (https://www.facebook.com/freegamemusic/)  
An excellent source for royalty-free music to use in your games/projects.
