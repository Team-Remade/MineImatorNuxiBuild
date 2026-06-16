#version 330 core

out float FragMask;

void main() {
    // Write 1.0 wherever a selected object covers a pixel.
    FragMask = 1.0;
}
